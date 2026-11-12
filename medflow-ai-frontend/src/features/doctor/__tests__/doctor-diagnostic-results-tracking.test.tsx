import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DoctorDiagnosticResultsTrackingPage } from '../pages/doctor-diagnostic-results-tracking-page';
import type { DoctorDiagnosticTrackingResponseDto } from '../types/doctor-diagnostic-tracking.types';

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
      <MemoryRouter initialEntries={['/doctor/diagnostic-results/tracking']}>
        <DoctorDiagnosticResultsTrackingPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const API_PAYLOAD_FIXTURE: DoctorDiagnosticTrackingResponseDto = {
  date: '2026-11-09',
  hospitalId: 1,
  doctorId: 10,
  doctorCode: 'DOC-010',
  doctorName: 'BS. Trần Minh Khoa',
  generatedAtUtc: '2026-11-09T03:00:00Z',
  stats: {
    totalVisits: 1,
    completedVisits: 0,
    inProgressVisits: 0,
    pendingSignVisits: 1,
    totalOrders: 2,
    signedOrders: 1,
    waitingOrders: 1,
  },
  totalCount: 1,
  page: 1,
  pageSize: 20,
  totalPages: 1,
  items: [
    {
      visitId: 9001,
      visitCode: 'VIS-202611-0001',
      visitStatus: 'IN_PROGRESS',
      patientId: 501,
      patientCode: 'BN-000501',
      patientName: 'Lê Thị Hoa',
      trackingStatus: 'PENDING_SIGN',
      totalOrders: 2,
      signedOrders: 1,
      awaitingSignOrders: 1,
      inProgressOrders: 0,
      pendingOrders: 0,
      mandatoryOrders: 1,
      mandatorySignedOrders: 1,
      completionPercent: 50,
      latestOrderedAt: '2026-11-09T09:15:00',
      orders: [
        {
          orderId: 701,
          orderCode: 'CLS-202611-0701',
          serviceId: 1,
          serviceCode: 'LAB-CBC',
          serviceName: 'Công thức máu tổng quát',
          priority: 'NORMAL',
          requiredForConclusion: true,
          orderStatus: 'COMPLETED',
          trackingStatus: 'SIGNED',
          resultStatus: 'SIGNED',
          orderingDoctorId: 10,
          orderingDoctorName: 'BS. Trần Minh Khoa',
          orderedAt: '2026-11-09T08:40:00',
          assigneeStaffName: 'KTV. Lê Thu Hà',
          resultSummary: 'Hb 135 g/L',
          signedAt: '2026-11-09T09:05:00',
          signedByStaffName: 'KTV. Lê Thu Hà',
        },
        {
          orderId: 702,
          orderCode: 'CLS-202611-0702',
          serviceId: 2,
          serviceCode: 'IMG-ABD-US',
          serviceName: 'Siêu âm ổ bụng',
          priority: 'URGENT',
          requiredForConclusion: false,
          orderStatus: 'IN_PROGRESS',
          trackingStatus: 'AWAITING_SIGN',
          resultStatus: 'DRAFT',
          orderingDoctorId: 10,
          orderingDoctorName: 'BS. Trần Minh Khoa',
          orderedAt: '2026-11-09T09:15:00',
          assigneeStaffName: 'BS. Phạm Quốc Việt',
          resultSummary: 'Gan nhiễm mỡ độ 1',
          signedAt: null,
          signedByStaffName: null,
        },
      ],
    },
  ],
};

describe('DoctorDiagnosticResultsTrackingPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: {
        id: 10,
        email: 'doctor@medflow.vn',
        fullName: 'BS. Trần Minh Khoa',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: null,
      refreshToken: null,
      isAuthenticated: true,
    });
  });

  it('calls GET /doctor/diagnostic-results/tracking when token is present and renders stats, table, and summary modal', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_PAYLOAD_FIXTURE });

    renderPage();

    expect(await screen.findByText('VIS-202611-0001')).toBeInTheDocument();

    expect(getSpy).toHaveBeenCalledTimes(1);
    const [url, config] = getSpy.mock.calls[0];
    expect(url).toBe('/doctor/diagnostic-results/tracking');
    expect(config?.params).toEqual({ page: 1, pageSize: 20 });
    expect(config?.params).not.toHaveProperty('status');

    expect(screen.getByTestId('tracking-stat-totalVisits')).toHaveTextContent('01');
    expect(screen.getByTestId('tracking-stat-pendingSignVisits')).toHaveTextContent('01');
    expect(screen.getByText('1/2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Xem tổng hợp KQ VIS-202611-0001' }));

    expect(await screen.findByText('Hb 135 g/L')).toBeInTheDocument();
    expect(screen.getByText('Đã ký duyệt')).toBeInTheDocument();
    expect(screen.getByTestId('summary-order-CLS-202611-0702')).toHaveTextContent('Chờ ký duyệt');
  });

  it('passes server-side status and keyword filters in query params', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_PAYLOAD_FIXTURE });

    renderPage();
    await screen.findByText('VIS-202611-0001');

    fireEvent.click(screen.getByRole('button', { name: 'Lọc Chờ ký duyệt' }));

    await waitFor(() => {
      const latestParams = getSpy.mock.calls.at(-1)?.[1]?.params as
        Record<string, unknown> | undefined;
      expect(latestParams?.status).toBe('PENDING_SIGN');
      expect(latestParams?.page).toBe(1);
    });

    fireEvent.change(screen.getByLabelText('Tìm kiếm lượt khám CLS'), {
      target: { value: 'BN-000103' },
    });

    await waitFor(() => {
      const latestParams = getSpy.mock.calls.at(-1)?.[1]?.params as
        Record<string, unknown> | undefined;
      expect(latestParams?.keyword).toBe('BN-000103');
      expect(latestParams?.status).toBe('PENDING_SIGN');
    });
  });

  it('blocks API request and displays error message when client filter validation fails', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_PAYLOAD_FIXTURE });

    renderPage();
    await screen.findByText('VIS-202611-0001');
    const initialCalls = getSpy.mock.calls.length;

    const searchInput = screen.getByLabelText('Tìm kiếm lượt khám CLS');
    fireEvent.change(searchInput, { target: { value: 'a'.repeat(101) } });

    expect(await screen.findByTestId('tracking-filter-error')).toHaveTextContent(
      'Từ khóa tìm kiếm không hợp lệ (tối đa 100 ký tự, không chứa ký tự < >).',
    );
    expect(getSpy.mock.calls.length).toBe(initialCalls);

    fireEvent.change(searchInput, { target: { value: '<script>' } });

    expect(screen.getByTestId('tracking-filter-error')).toHaveTextContent(
      'Từ khóa tìm kiếm không hợp lệ (tối đa 100 ký tự, không chứa ký tự < >).',
    );
    expect(getSpy.mock.calls.length).toBe(initialCalls);
  });

  it('surfaces HTTP 403 error banner instead of falling back to mock data', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(
      new AxiosError(
        'Forbidden',
        '403',
        undefined,
        {},
        {
          status: 403,
          statusText: 'Forbidden',
          headers: {},
          config: {} as never,
          data: { detail: 'Bạn không có quyền xem dữ liệu này.' },
        },
      ),
    );

    renderPage();

    expect(await screen.findByTestId('tracking-error-banner')).toHaveTextContent(
      'Bạn không có quyền xem dữ liệu này.',
    );
    expect(screen.queryByText('VIS-2026-0101')).toBeNull();
  });

  it('falls back to mock data when no token is present and supports status filter and modal', async () => {
    renderPage();

    expect(await screen.findByText('VIS-2026-0102')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Xem tổng hợp KQ VIS-2026-0102' }));
    const orderItem = await screen.findByTestId('summary-order-CLS-2026-0105');
    expect(orderItem).toHaveTextContent('Siêu âm ổ bụng');
    expect(orderItem).toHaveTextContent('Chờ ký duyệt');

    fireEvent.click(screen.getByRole('button', { name: 'Đóng tổng hợp kết quả' }));

    fireEvent.click(screen.getByRole('button', { name: 'Lọc Hoàn tất 100%' }));
    await waitFor(() => {
      expect(screen.queryByText('VIS-2026-0102')).toBeNull();
    });
    expect(screen.getByText('VIS-2026-0101')).toBeInTheDocument();
  });

  it('guards /doctor/diagnostic-results/tracking against DIAGNOSTIC_STAFF role', async () => {
    useAuthStore.setState({
      user: {
        id: 301,
        email: 'diag@medflow.vn',
        fullName: 'KTV. Lê Thu Hà',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/diagnostic-results/tracking']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).not.toHaveTextContent(
        '/doctor/diagnostic-results/tracking',
      );
    });
  });
});
