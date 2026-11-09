import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import * as doctorApi from '../api/doctor.api';
import { DOCTOR_DASHBOARD_MOCK } from '../data/doctor-mock-data';
import { DoctorDashboardPage } from '../pages/doctor-dashboard-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/doctor']}>
        <DoctorDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorDashboardPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });
  it('renders doctor header, identity, and 4 clinical stat cards', () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'bacsi.hung@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Tổng quan bàn khám bác sĩ')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Nguyễn Văn Hùng').length).toBeGreaterThan(0);
    expect(screen.getByText('Chờ khám tại phòng')).toBeInTheDocument();
    expect(screen.getByText('Đang khám lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Chờ duyệt kết quả CLS')).toBeInTheDocument();
    expect(screen.getByText('Hoàn tất trong ca')).toBeInTheDocument();
  });

  it('renders assigned patient queue and pending diagnostic results', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Hàng đợi bệnh nhân được phân công')).toBeInTheDocument();
    expect(screen.getByText('B008')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Điện tâm đồ 12 chuyển đạo (ECG)')).toBeInTheDocument();
  });

  it('filters assigned queue by status and search keyword and shows empty state when unmatched', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Chờ CLS' }));
    expect(screen.getByText('Võ Minh Tuấn')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Tất cả' }));
    const searchInput = screen.getByLabelText('Tìm bệnh nhân trong hàng đợi');
    fireEvent.change(searchInput, { target: { value: 'Trần Thị Mai' } });

    expect(screen.getByText('Trần Thị Mai')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: 'KHONG_TON_TAI_999' } });
    expect(screen.getByText('Không có bệnh nhân phù hợp bộ lọc hiện tại.')).toBeInTheDocument();
  });

  it('exposes only Doctor-authorized entrypoints and hides reception/admin actions', () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'bacsi.hung@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    // Lối vào nghiệp vụ của Doctor
    expect(screen.getByText('Gọi bệnh nhân tiếp theo')).toBeInTheDocument();
    expect(screen.getByText('Mở phiếu khám lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Chỉ định cận lâm sàng')).toBeInTheDocument();
    expect(screen.getAllByText('Kê đơn thuốc điện tử').length).toBeGreaterThan(0);

    // Không hiển thị nghiệp vụ của Lễ tân / Admin
    expect(screen.queryByText('Check-in & Tiếp nhận')).not.toBeInTheDocument();
    expect(screen.queryByText('Quản lý tài khoản nhân viên')).not.toBeInTheDocument();
  });

  it('handles API error state and allows retry (Thử lại)', async () => {
    vi.spyOn(doctorApi, 'fetchDoctorDashboardSummary')
      .mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ bàn khám bác sĩ'))
      .mockResolvedValueOnce(DOCTOR_DASHBOARD_MOCK);

    useAuthStore.setState({
      user: {
        id: 3,
        email: 'doctor01@medflow.test',
        fullName: 'BS. Nguyễn Minh Đức',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
      accessToken: 'tok-doctor',
      refreshToken: 'rtok-doctor',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi kết nối máy chủ bàn khám bác sĩ')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(screen.queryByText('Lỗi kết nối máy chủ bàn khám bác sĩ')).not.toBeInTheDocument();
    });
  });

  it('calls GET /doctor/dashboard/summary when token is present and syncs doctorProfile from BE', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...DOCTOR_DASHBOARD_MOCK,
        facilityName: 'Bệnh viện Trung ương MedFlow',
        facilityCode: 'HOS-001',
        roomCode: 'RM-001',
        roomName: 'Khám Tiêu hóa 101',
        departmentName: 'Khoa Tiêu hóa',
        shiftLabel: 'Ca sáng 07:30–11:30',
        doctorProfile: {
          userId: 3,
          doctorId: 1,
          doctorCode: 'DOC-001',
          staffCode: 'STF-001',
          email: 'doctor01@medflow.test',
          fullName: 'Nguyễn Minh Đức',
          professionalTitle: 'BS.CKI',
          displayName: 'BS.CKI Nguyễn Minh Đức',
          specialtyName: 'Tiêu hóa',
          departmentId: 2,
          departmentName: 'Khoa Tiêu hóa',
          hospitalId: 1,
          hospitalCode: 'HOS-001',
          hospitalName: 'Bệnh viện Trung ương MedFlow',
          roomCode: 'RM-001',
          roomName: 'Khám Tiêu hóa 101',
          licenseNumber: 'DEMO-CCHN-001',
          roles: ['DOCTOR'],
          permissions: [
            'DIAGNOSTIC_ORDER',
            'MEDICAL_RECORD_WRITE',
            'PAYMENT_VIEW',
            'PRESCRIPTION_ISSUE',
          ],
        },
        generatedAtUtc: '2026-04-20T09:00:00Z',
      },
    });

    useAuthStore.getState().setAuth({
      user: {
        id: 3,
        email: 'doctor01@medflow.test',
        fullName: null,
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: null,
      },
      accessToken: 'jwt-doctor-token',
      refreshToken: 'jwt-doctor-refresh',
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/dashboard/summary', undefined);
      expect(screen.getAllByText('BS.CKI Nguyễn Minh Đức').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('Mã NV: STF-001')).toBeInTheDocument();
    expect(screen.getAllByText(/RM-001 • Khoa Tiêu hóa/).length).toBeGreaterThan(0);
  });

  it('protects /doctor route with RequireAuth and RequireDoctor', () => {
    // 1. Unauthenticated -> redirect /login?returnUrl=%2Fdoctor
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/doctor']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan bàn khám bác sĩ')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/login?returnUrl=%2Fdoctor');
    unmount();

    // 2. Authenticated with PATIENT role -> redirect to /
    useAuthStore.setState({
      user: {
        id: 1,
        email: 'patient01@medflow.test',
        fullName: 'Nguyễn Văn An',
        roles: ['PATIENT'],
        patientCode: 'PAT-0001',
      },
      accessToken: 'tok-patient',
      refreshToken: 'rtok-patient',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/doctor']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan bàn khám bác sĩ')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
  });

  it('triggers Call Next Eligible Patient when clicking "Gọi bệnh nhân tiếp theo" quick action and updates queue row status (Issue #93 / UC31)', async () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'bacsi.hung@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        servicePointId: 'sp-room-102',
        outcome: 'CALLED',
        message: 'Đã gọi bệnh nhân Lê Hoàng Nam (phiếu B005) vào phòng P.102.',
        selectionReason: 'RETURN_CONSULTATION',
        calledEntry: {
          id: 'dq-3',
          queueNumber: 5,
          ticketNo: 'B005',
          servingOrder: 1,
          visitCode: 'VIS-2026-0089',
          patientName: 'Lê Hoàng Nam',
          patientCode: 'BN-2026-0089',
          visitType: 'WALK_IN',
          servicePointId: 'sp-room-102',
          roomCode: 'P.102',
          roomName: 'Phòng khám Nội tổng quát 102',
          departmentName: 'Khoa Nội Tổng Quát',
          doctorName: 'BS. CKII Nguyễn Văn Hùng',
          status: 'CALLED',
          priority: 'NORMAL',
          queueType: 'RETURN_CONSULTATION',
          presenceStatus: 'PRESENT',
          arrivalClass: 'WALK_IN',
          scheduledAt: null,
          checkedInAt: '08:25',
          calledAt: '08:35',
          startedAt: null,
          blockedReason: null,
          isEligible: false,
          isAppointmentProtected: false,
          isBackfilled: false,
          backfillReason: null,
          queueGroup: 'CALLED',
          ineligibleReason: null,
        },
        previousStateVersion: 1,
        stateVersion: 2,
        updatedAtUtc: '2026-11-07T01:35:00Z',
        entries: [],
      },
    });

    renderPage();

    fireEvent.click(screen.getByText('Gọi bệnh nhân tiếp theo'));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/queue/service-points/sp-room-102/call-next', {});
    });

    const statusBanner = await screen.findByRole('status');
    expect(statusBanner).toHaveTextContent(
      'Đã gọi bệnh nhân Lê Hoàng Nam (phiếu B005) vào phòng P.102.',
    );
    expect(statusBanner).toHaveTextContent('Lý do chọn: Đọc KQ CLS');

    const patientRow = screen
      .getAllByText('B005')
      .map((el) => el.closest('tr'))
      .find((row) => row !== null);
    expect(patientRow).toBeDefined();
    expect(patientRow).toHaveTextContent('Đã gọi vào');
  });
});
