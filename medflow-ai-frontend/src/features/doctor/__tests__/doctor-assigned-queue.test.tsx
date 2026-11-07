import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DOCTOR_ASSIGNED_QUEUE_MOCK } from '../data/doctor-assigned-queue-mock';
import { DoctorAssignedQueuePage } from '../pages/doctor-assigned-queue-page';

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
      <MemoryRouter initialEntries={['/doctor/assigned-queue']}>
        <DoctorAssignedQueuePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function extractTicketNos(table: HTMLElement): string[] {
  const rows = within(table).getAllByRole('row').slice(1);
  return rows.map((row) => {
    const cells = within(row).getAllByRole('cell');
    const match = cells[1]?.textContent?.match(/A\d{3}/);
    return match ? match[0] : '';
  });
}

describe('DoctorAssignedQueuePage', () => {
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

  it('renders entries in exact Backend order without client-side re-sorting and shows eligibility metadata', async () => {
    renderPage();

    const table = await screen.findByRole('table', { name: 'Hàng đợi P.102' });
    const tickets = extractTicketNos(table);

    // A015 appears before A014 in Backend servingOrder (no ticket/queueNumber sorting on FE)
    expect(tickets).toEqual([
      'A012',
      'A013',
      'A015',
      'A014',
      'A016',
      'A017',
      'A018',
      'A010',
      'A009',
    ]);

    expect(screen.getByText('Chờ kết quả CLS')).toBeInTheDocument();
    expect(screen.getByText('Giữ chỗ theo giờ hẹn')).toBeInTheDocument();
    expect(screen.getByText('Giữ chỗ theo khung giờ hẹn 10:30')).toBeInTheDocument();
    expect(screen.getByText('Kế tiếp đủ điều kiện')).toBeInTheDocument();
    expect(screen.getAllByText('A015').length).toBeGreaterThanOrEqual(2);
  });

  it('filters by queueGroup and keyword while preserving Backend order and shows empty filter state', async () => {
    renderPage();

    await screen.findByRole('table', { name: 'Hàng đợi P.102' });

    fireEvent.click(screen.getByRole('button', { name: /Chưa đủ điều kiện \(3\)/ }));
    const filteredTable = screen.getByRole('table', { name: 'Hàng đợi P.102' });
    expect(extractTicketNos(filteredTable)).toEqual(['A016', 'A017', 'A018']);

    fireEvent.click(screen.getByRole('button', { name: /Tất cả \(9\)/ }));
    const searchInput = screen.getByLabelText('Tìm bệnh nhân trong hàng đợi');
    fireEvent.change(searchInput, { target: { value: 'Phạm Văn Lộc' } });

    const searchedTable = screen.getByRole('table', { name: 'Hàng đợi P.102' });
    expect(extractTicketNos(searchedTable)).toEqual(['A015']);

    fireEvent.change(searchInput, { target: { value: 'KHONG_TON_TAI_999' } });
    expect(screen.getByText('Không có bệnh nhân phù hợp bộ lọc hiện tại.')).toBeInTheDocument();
  });

  it('calls GET /doctor/assigned-queue without params initially and passes queueDate when date picker changes', async () => {
    localStorage.setItem('medflow.access_token', 'jwt-doctor');
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'doctor01@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'jwt-doctor',
      refreshToken: 'rtok-doctor',
      isAuthenticated: true,
    });

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValue({ data: DOCTOR_ASSIGNED_QUEUE_MOCK });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/assigned-queue', undefined);
    });

    fireEvent.change(screen.getByLabelText('Ngày hàng đợi'), {
      target: { value: '2026-11-06' },
    });

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/assigned-queue', {
        params: { queueDate: '2026-11-06' },
      });
    });
  });

  it('shows empty assignment state when doctor has no assigned service points on the selected date', async () => {
    localStorage.setItem('medflow.access_token', 'jwt-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...DOCTOR_ASSIGNED_QUEUE_MOCK,
        servicePoints: [],
        stats: {
          totalEntries: 0,
          inProgressCount: 0,
          calledCount: 0,
          eligibleCount: 0,
          notEligibleCount: 0,
          skippedCount: 0,
          completedCount: 0,
        },
      },
    });

    renderPage();

    await waitFor(() => {
      expect(
        screen.getByText('Chưa có điểm khám nào được phân công cho bác sĩ trong ngày này.'),
      ).toBeInTheDocument();
    });
  });

  it('does not mask HTTP 404 domain errors with mock data and recovers on retry', async () => {
    localStorage.setItem('medflow.access_token', 'jwt-doctor');

    const notFoundError = new AxiosError('nf', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 404,
      statusText: 'Not Found',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: { detail: 'Không tìm thấy hồ sơ bác sĩ' },
    });

    vi.spyOn(apiClient, 'get')
      .mockRejectedValueOnce(notFoundError)
      .mockResolvedValueOnce({ data: DOCTOR_ASSIGNED_QUEUE_MOCK });

    renderPage();

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Không tìm thấy hồ sơ bác sĩ');
    });
    expect(screen.queryByText('A012')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByRole('table', { name: 'Hàng đợi P.102' })).toBeInTheDocument();
    });
  });

  it('enforces RequireAuth / RequireDoctor on /doctor/assigned-queue and navigates from Doctor sidebar', async () => {
    // 1. Unauthenticated -> /login?returnUrl=%2Fdoctor%2Fassigned-queue
    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/doctor/assigned-queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdoctor%2Fassigned-queue',
    );
    unmount();

    // 2. Authenticated DOCTOR at /doctor -> click sidebar "Hàng đợi phân công" -> /doctor/assigned-queue
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'doctor01@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'jwt-doctor',
      refreshToken: 'rtok-doctor',
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

    fireEvent.click(screen.getByRole('button', { name: /Hàng đợi phân công/ }));

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor/assigned-queue');
      expect(
        screen.getByRole('heading', { name: 'Hàng đợi được phân công (UC30)' }),
      ).toBeInTheDocument();
    });
  });
});
