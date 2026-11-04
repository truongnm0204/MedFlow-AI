import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import * as hospitalAdminApi from '../api/hospital-admin.api';
import { HOSPITAL_ADMIN_DASHBOARD_MOCK } from '../data/hospital-admin-mock-data';
import { HospitalAdminDashboardPage } from '../pages/hospital-admin-dashboard-page';

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
      <MemoryRouter initialEntries={['/hospital-admin']}>
        <HospitalAdminDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('HospitalAdminDashboardPage (SCR-ADM-DASH-01)', () => {
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

  it('render dashboard header, sidebar dung vai tro Hospital Admin, 4 KPI stat cards va an chuc nang trai quyen', async () => {
    useAuthStore.setState({
      user: {
        id: 50,
        email: 'admin.bv@medflow.test',
        fullName: 'ThS. Phạm Quốc Bảo',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'ADM-019',
      },
      accessToken: 'tok-admin',
      refreshToken: 'rtok-admin',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Tổng quan quản trị bệnh viện')).toBeInTheDocument();
    expect(screen.getAllByText('ThS. Phạm Quốc Bảo').length).toBeGreaterThan(0);
    expect(screen.getByText('Mã NV: ADM-019')).toBeInTheDocument();

    const overviewNavLink = screen.getByRole('link', { name: 'Tổng quan cơ sở' });
    expect(overviewNavLink).toHaveAttribute('aria-current', 'page');
    expect(overviewNavLink).toHaveAttribute('href', '/hospital-admin');

    await waitFor(() => {
      expect(screen.getByText('Khoa phòng hoạt động')).toBeInTheDocument();
    });

    // 4 KPI stat cards
    expect(screen.getByText('Khoa phòng hoạt động')).toBeInTheDocument();
    expect(screen.getByText('Buồng khám sẵn sàng')).toBeInTheDocument();
    expect(screen.getByText('Bác sĩ & Dịch vụ mở đặt hẹn')).toBeInTheDocument();
    expect(screen.getByText('Slot khám đang mở hôm nay')).toBeInTheDocument();

    // 6 lối vào chức năng được phép của Hospital Admin (UC47–UC56)
    expect(screen.getByText('Quản lý Khoa phòng')).toBeInTheDocument();
    expect(screen.getByText('Quản lý Buồng khám')).toBeInTheDocument();
    expect(screen.getByText('Quản lý Bác sĩ')).toBeInTheDocument();
    expect(screen.getByText('Quản lý Dịch vụ khám')).toBeInTheDocument();
    expect(screen.getAllByText('Quản lý Slot khám').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Báo cáo vận hành').length).toBeGreaterThan(0);

    // Không hiển thị các chức năng trái quyền (System Admin UC57–UC60 & Clinical Workbench)
    expect(screen.queryByText('Quản lý tài khoản nhân sự')).not.toBeInTheDocument();
    expect(screen.queryByText('Nhật ký kiểm toán (Audit Log)')).not.toBeInTheDocument();
    expect(screen.queryByText('Khám lâm sàng & EMR')).not.toBeInTheDocument();
    expect(screen.queryByText('Kê đơn thuốc điện tử')).not.toBeInTheDocument();
    expect(screen.queryByText('Xác nhận cấp phát')).not.toBeInTheDocument();
  });

  it('render bang danh muc cau hinh, chon hang muc cap nhat khung xem nhanh, chon quick action va fallback khi user.fullName null', async () => {
    useAuthStore.setState({
      user: {
        id: 51,
        email: 'admin.fallback@medflow.test',
        fullName: null,
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-admin',
      refreshToken: 'rtok-admin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('DEP-001')).toBeInTheDocument();
    });

    expect(screen.getAllByText('ThS. Nguyễn Văn Quản').length).toBeGreaterThan(0);
    expect(screen.getByText('Mã NV: ADM-001')).toBeInTheDocument();
    expect(screen.getByText('DOC-0142')).toBeInTheDocument();
    expect(screen.getAllByText('RM-104').length).toBeGreaterThan(0);
    expect(screen.getAllByText('SLT-009').length).toBeGreaterThan(0);

    // Chọn một hạng mục khác trong bảng để cập nhật khung xem nhanh
    fireEvent.click(screen.getByText('DOC-0142'));
    expect(screen.getByText(/Đang chọn: DOC-0142 • BS\. CKI Lê Hoàng Minh/)).toBeInTheDocument();

    // Tương tác Quick Action
    fireEvent.click(screen.getByText('Quản lý Khoa phòng'));
    expect(screen.getByText(/Đang chọn lối vào:/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Mở Quản lý Khoa phòng' })).toHaveAttribute(
      'href',
      '/admin/departments',
    );
  });

  it('loc danh muc cau hinh theo nhom, trang thai, tu khoa tim kiem va khoi phuc khi dat lai bo loc', async () => {
    useAuthStore.setState({
      user: {
        id: 50,
        email: 'admin.bv@medflow.test',
        fullName: 'ThS. Phạm Quốc Bảo',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-admin',
      refreshToken: 'rtok-admin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('DEP-001')).toBeInTheDocument();
    });

    // Lọc theo nhóm "Lịch & Slot khám"
    fireEvent.click(screen.getByRole('button', { name: 'Lịch & Slot khám' }));
    expect(screen.getByText('Khung giờ khám Nội tiết (08:00 - 08:30)')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();

    // Đổi về "Tất cả" và lọc theo trạng thái MAINTENANCE
    fireEvent.click(screen.getByRole('button', { name: 'Tất cả' }));
    const statusSelect = screen.getByLabelText('Lọc trạng thái cấu hình');
    fireEvent.change(statusSelect, { target: { value: 'MAINTENANCE' } });
    expect(screen.getByText('Phòng khám Tim mạch 2')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();

    // Đổi trạng thái về ALL và lọc theo từ khóa "SLT-009"
    fireEvent.change(statusSelect, { target: { value: 'ALL' } });
    const searchInput = screen.getByLabelText('Tìm kiếm hạng mục cấu hình cơ sở');
    fireEvent.change(searchInput, { target: { value: 'SLT-009' } });
    expect(screen.getByText('Khung giờ khám Nội tiết (08:00 - 08:30)')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();

    // Tìm kiếm từ khóa không tồn tại → hiển thị empty state + nút Đặt lại bộ lọc
    fireEvent.change(searchInput, { target: { value: 'KHONG-TON-TAI-999' } });
    expect(
      screen.getByText('Không tìm thấy hạng mục cấu hình phù hợp với bộ lọc hiện tại.'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Đặt lại bộ lọc/i }));
    expect(screen.getByText('DEP-001')).toBeInTheDocument();
    expect(screen.getByText('BS. CKI Lê Hoàng Minh')).toBeInTheDocument();
  });

  it('xu ly trang thai loi API va cho phep thu lai (retry)', async () => {
    vi.spyOn(hospitalAdminApi, 'fetchHospitalAdminDashboardSummary')
      .mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ quản trị'))
      .mockResolvedValueOnce(HOSPITAL_ADMIN_DASHBOARD_MOCK);

    useAuthStore.setState({
      user: {
        id: 50,
        email: 'admin.bv@medflow.test',
        fullName: 'ThS. Phạm Quốc Bảo',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-admin',
      refreshToken: 'rtok-admin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi kết nối máy chủ quản trị')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(screen.getByText('Khoa phòng hoạt động')).toBeInTheDocument();
    });
  });

  it('goi truc tiep GET /admin/hospital-dashboard/summary khi co token va dong bo adminProfile tu BE', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...HOSPITAL_ADMIN_DASHBOARD_MOCK,
        facilityName: 'Bệnh viện Trung ương MedFlow',
        facilityCode: 'HOS-001',
        adminProfile: {
          userId: 11,
          email: 'hospital_admin01@medflow.test',
          fullName: 'TS. BS Vũ Quốc Tuấn',
          staffCode: 'STF-009',
          hospitalId: 1,
          hospitalCode: 'HOS-001',
          hospitalName: 'Bệnh viện Trung ương MedFlow',
          roles: ['HOSPITAL_ADMIN'],
          permissions: ['HOSPITAL_CONFIG', 'PAYMENT_VIEW', 'QUEUE_MANAGE', 'VISIT_ASSIGN'],
        },
        generatedAtUtc: '2026-04-20T09:00:00Z',
      },
    });

    useAuthStore.getState().setAuth({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: null,
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: null,
      },
      accessToken: 'jwt-live-token',
      refreshToken: 'jwt-refresh-token',
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/admin/hospital-dashboard/summary', undefined);
      expect(screen.getAllByText('TS. BS Vũ Quốc Tuấn').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('Mã NV: STF-009')).toBeInTheDocument();
    expect(screen.getAllByText('Bệnh viện Trung ương MedFlow').length).toBeGreaterThan(0);
  });

  it('bao ve route /hospital-admin bang RequireAuth va RequireAdmin', () => {
    // 1. Chưa đăng nhập → redirect /login?returnUrl=%2Fhospital-admin
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/hospital-admin']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan quản trị bệnh viện')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fhospital-admin',
    );
    unmount();

    // 2. Đã đăng nhập nhưng role PATIENT (không phải Admin) → redirect về /
    useAuthStore.setState({
      user: {
        id: 10,
        email: 'patient@medflow.test',
        fullName: 'Nguyễn Văn An',
        roles: ['PATIENT'],
        patientCode: 'PAT-001',
      },
      accessToken: 'tok-patient',
      refreshToken: 'rtok-patient',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/hospital-admin']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan quản trị bệnh viện')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
  });
});
