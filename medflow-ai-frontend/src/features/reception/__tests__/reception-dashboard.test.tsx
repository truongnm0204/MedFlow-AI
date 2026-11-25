import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { buildMockExaminationQueueResponse } from '../data/examination-queue-mock';
import {
  COMPATIBLE_EXAMINATION_ROOMS_MOCK,
  COMPATIBLE_TRANSFER_ROOMS_MOCK,
} from '../data/reception-mock-data';
import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ReceptionDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ReceptionDashboardPage', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (url === '/reception/examination-queue') {
        return { data: buildMockExaminationQueueResponse() };
      }
      if (url === '/reception/dashboard-summary') {
        return {
          data: {
            waitingCheckIn: 24,
            inQueue: 18,
            inConsultation: 8,
            completedToday: 46,
          },
        };
      }
      if (url === '/reception/patients/search') {
        return {
          data: {
            items: [],
            totalCount: 0,
            page: 1,
            pageSize: 50,
            counts: { all: 0, hasAppointment: 0, checkedIn: 0, noAppointment: 0 },
          },
        };
      }
      if (typeof url === 'string' && url.endsWith('/compatible-rooms')) {
        return {
          data: {
            visitId: 102,
            visitCode: 'VIS-2026-0102',
            visitStatus: 'CALLED',
            currentRoomId: 102,
            currentRoomCode: 'P.102',
            currentRoomName: 'Phòng khám Nội tổng quát 1',
            departmentId: 1,
            departmentCode: 'NOI',
            departmentName: 'Khoa Nội tổng quát',
            rooms: COMPATIBLE_TRANSFER_ROOMS_MOCK,
          },
        };
      }
      if (typeof url === 'string' && url.endsWith('/available-rooms')) {
        return {
          data: {
            visitId: 106,
            visitCode: 'VIS-2026-0106',
            visitStatus: 'CHECKED_IN',
            hospitalId: 1,
            assignedDoctorId: 5,
            assignedDoctorCode: 'DOC-05',
            assignedDoctorName: 'BS. Lê Hoàng Nam',
            currentExaminationRoomId: null,
            currentExaminationRoomCode: null,
            requiredDepartmentId: 2,
            requiredDepartmentName: 'Khoa Tim mạch',
            rooms: COMPATIBLE_EXAMINATION_ROOMS_MOCK,
          },
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });
  it('render dashboard header + stat cards', () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    // Label "Tổng quan quầy tiếp đón" xuất hiện ở cả sidebar nav và page header.
    expect(screen.getAllByText('Tổng quan quầy tiếp đón').length).toBeGreaterThan(0);
    expect(screen.getByText('Chờ check-in')).toBeInTheDocument();
    expect(screen.getByText('Đang trong hàng đợi')).toBeInTheDocument();
    // "Đang khám" xuất hiện ở cả stat card và queue status badge.
    expect(screen.getAllByText('Đang khám').length).toBeGreaterThan(0);
    expect(screen.getByText('Hoàn tất hôm nay')).toBeInTheDocument();
  });

  it('hien thi so lieu KPI thuc tu endpoint /reception/dashboard-summary va khong hien so 0 gia khi loi', async () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    const { unmount } = renderPage();

    expect(await screen.findByText('24')).toBeInTheDocument();
    expect(screen.getByTestId('reception-stat-waitingCheckIn')).toHaveTextContent('24');
    expect(screen.getByTestId('reception-stat-inQueue')).toHaveTextContent('18');
    expect(screen.getByTestId('reception-stat-inConsultation')).toHaveTextContent('8');
    expect(screen.getByTestId('reception-stat-completedToday')).toHaveTextContent('46');
    unmount();

    // Khi API dashboard-summary bị lỗi, hiển thị ký hiệu — chứ không giả lập số 0
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (url === '/reception/examination-queue') {
        return { data: buildMockExaminationQueueResponse() };
      }
      if (url === '/reception/dashboard-summary') {
        throw new Error('Backend unavailable');
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    renderPage();

    expect(await screen.findByLabelText('Chờ check-in: chưa có dữ liệu')).toHaveTextContent('—');
    expect(screen.getByLabelText('Hoàn tất hôm nay: chưa có dữ liệu')).toHaveTextContent('—');
  });

  it('render queue table voi mock entries', async () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Hàng đợi hiện tại')).toBeInTheDocument();
    expect(await screen.findByText('A012')).toBeInTheDocument();
    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
  });

  it('mo modal chuyen phong tuong thich tu bang hang doi va khoa ca dang kham', async () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    const user = userEvent.setup();
    renderPage();

    const inProgressBtn = await screen.findByRole('button', {
      name: 'Chuyển phòng khám cho Nguyễn Văn An',
    });
    expect(inProgressBtn).toBeDisabled();

    const calledBtn = await screen.findByRole('button', {
      name: 'Chuyển phòng khám cho Trần Thị Bích',
    });
    expect(calledBtn).not.toBeDisabled();

    await user.click(calledBtn);

    expect(
      await screen.findByText('Chuyển bệnh nhân sang phòng khám tương thích (UC27)'),
    ).toBeInTheDocument();
  });

  it('mo modal phan buong kham tu hang doi tiep don', async () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'reception01@medflow.test',
        fullName: 'Lễ tân Lê Lan',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok-rec',
      refreshToken: 'rtok-rec',
      isAuthenticated: true,
    });

    const user = userEvent.setup();
    renderPage();

    await user.click(
      await screen.findByRole('button', { name: 'Phân buồng khám cho Vũ Thị Phương' }),
    );

    expect(await screen.findByText('Phân buồng khám lâm sàng (UC27)')).toBeInTheDocument();
    expect(screen.getByDisplayValue('VIS-2026-0106')).toBeInTheDocument();
  });

  it('an thao tac chuyen buong kham khi nguoi dung khong co quyen VISIT_ASSIGN', () => {
    useAuthStore.setState({
      user: {
        id: 99,
        email: 'staff@medflow.test',
        fullName: 'Nhân viên không có quyền điều phối',
        roles: ['PHARMACIST'],
        patientCode: null,
      },
      accessToken: 'tok-pharmacist',
      refreshToken: 'rtok-pharmacist',
      isAuthenticated: true,
    });

    renderPage();

    expect(
      screen.queryByRole('button', { name: 'Chuyển phòng khám cho Trần Thị Bích' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Chuyển phòng khám tương thích/i }),
    ).not.toBeInTheDocument();
  });

  it('bao ve route /reception bang RequireAuth va RequireReception', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/reception']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/login?returnUrl=%2Freception');
    unmount();

    useAuthStore.setState({
      user: {
        id: 1,
        email: 'patient@medflow.test',
        fullName: 'Bệnh nhân A',
        roles: ['PATIENT'],
        patientCode: 'PAT-001',
      },
      accessToken: 'tok-pat',
      refreshToken: 'rtok-pat',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/reception']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
  });
});
