import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { buildMockExaminationQueueResponse } from '../data/examination-queue-mock';
import { ExaminationQueuePage } from '../pages/examination-queue-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderQueuePage() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/reception/queue']}>
        <ExaminationQueuePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ExaminationQueuePage (UC28 - Issue #87)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân Tiếp Đón',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok-reception',
      refreshToken: 'rtok-reception',
      isAuthenticated: true,
    });
  });

  it('renders KPI stat cards, service point groups, current serving tickets, and 1-based serving order', async () => {
    renderQueuePage();
    expect(screen.getByText('Hàng đợi khám theo phòng & điểm dịch vụ (UC28)')).toBeInTheDocument();

    // KPI stat cards (wait for async query resolution)
    await waitFor(() => {
      expect(screen.getByText('Tổng lượt trong hàng đợi')).toBeInTheDocument();
    });
    expect(screen.getByText('Đang chờ gọi khám')).toBeInTheDocument();
    expect(screen.getByText('Đang gọi & Đang khám')).toBeInTheDocument();
    expect(screen.getByText('Hoàn tất / Qua lượt')).toBeInTheDocument();

    // Service points & room headers
    expect(screen.getByText('P.102')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Nội tổng quát 1')).toBeInTheDocument();
    expect(screen.getByText('P.103')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Tim mạch 1')).toBeInTheDocument();
    expect(screen.getByText('P.104')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Tai Mũi Họng')).toBeInTheDocument();

    // Current serving tickets & entries
    expect(screen.getAllByText('A012').length).toBeGreaterThan(0);
    expect(screen.getAllByText('B005').length).toBeGreaterThan(0);
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
    expect(screen.getByText('Lê Minh Cường')).toBeInTheDocument();
    expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();

    // 1-based serving order badges
    expect(screen.getAllByText('#1').length).toBe(3);
    expect(screen.getAllByText('#2').length).toBe(2);
    expect(screen.getAllByText('#3').length).toBe(2);
    expect(screen.getAllByText('#4').length).toBe(1);
  });

  it('filters queue entries by service point, status, priority, visitType, and keyword and resets filters from empty state', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    });

    // 1. Filter by service point P.103
    fireEvent.click(screen.getByRole('tab', { name: /P\.103 • Tim mạch 1/i }));
    expect(screen.getByText('Lê Minh Cường')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    // 2. Filter by status SKIPPED within P.103
    fireEvent.click(screen.getByRole('tab', { name: /Qua lượt/i }));
    expect(screen.getByText('Ngô Minh Tuấn')).toBeInTheDocument();
    expect(screen.getByText('Gọi 3 lần chưa có mặt tại cửa phòng khám')).toBeInTheDocument();
    expect(screen.queryByText('Lê Minh Cường')).not.toBeInTheDocument();

    // Reset back to ALL rooms & ALL statuses
    fireEvent.click(screen.getByRole('button', { name: /Đặt lại bộ lọc/i }));
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();

    // 3. Filter by priority URGENT and visitType WALK_IN
    fireEvent.change(screen.getByLabelText('Lọc theo độ ưu tiên'), {
      target: { value: 'URGENT' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo phân loại khám'), {
      target: { value: 'WALK_IN' },
    });
    expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    // 4. Filter by keyword with no matches -> empty state -> reset
    const searchInput = screen.getByLabelText('Tìm kiếm trong hàng đợi khám');
    fireEvent.change(searchInput, { target: { value: 'KHONG_CO_BENH_NHAN_999' } });

    expect(
      screen.getByText('Không có lượt khám nào trong hàng đợi khớp với bộ lọc'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Xóa bộ lọc' }));
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
  });

  it('validates filter form inputs with Zod and displays inline error message for invalid keyword', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    });

    const searchInput = screen.getByLabelText('Tìm kiếm trong hàng đợi khám');

    // Invalid keyword with angle brackets
    fireEvent.change(searchInput, { target: { value: '<script>alert(1)</script>' } });
    expect(screen.getByText('Từ khóa tìm kiếm chứa ký tự không hợp lệ.')).toBeInTheDocument();

    // Invalid keyword exceeding 100 chars
    fireEvent.change(searchInput, { target: { value: 'A'.repeat(101) } });
    expect(screen.getByText('Từ khóa tìm kiếm không được vượt quá 100 ký tự.')).toBeInTheDocument();

    // Valid keyword clears validation error
    fireEvent.change(searchInput, { target: { value: 'Hoàng Đức Em' } });
    expect(
      screen.queryByText('Từ khóa tìm kiếm không được vượt quá 100 ký tự.'),
    ).not.toBeInTheDocument();
    expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
  });

  it('enforces RBAC route guard on /reception/queue and hides management actions when QUEUE_MANAGE permission is absent', async () => {
    // 1. Unauthenticated user -> redirects to /login?returnUrl=%2Freception%2Fqueue
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount: unmount1 } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/reception/queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Freception%2Fqueue',
    );
    unmount1();

    // 2. Unauthorized role (DOCTOR) -> redirected away from /reception/queue
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'doctor@medflow.test',
        fullName: 'BS. Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok-doc',
      refreshToken: 'rtok-doc',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount: unmount2 } = render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/reception/queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor');
    unmount2();

    // 3. User without QUEUE_MANAGE permission -> hides "Thao tác" column & "Xem hồ sơ" buttons
    useAuthStore.setState({
      user: {
        id: 99,
        email: 'auditor@medflow.test',
        fullName: 'Kiểm Toán Viên',
        roles: ['SECURITY_AUDITOR'],
        patientCode: null,
      },
      accessToken: 'tok-auditor',
      refreshToken: 'rtok-auditor',
      isAuthenticated: true,
    });
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
      expect(screen.getByText('Chế độ chỉ xem')).toBeInTheDocument();
    });
    expect(screen.queryByText('Thao tác')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Xem hồ sơ' })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Xử lý đến sớm \/ Backfill/i }),
    ).not.toBeInTheDocument();
  });

  it('calls GET /reception/examination-queue when token is present and handles HTTP 500 error + retry', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

    const serverError = new AxiosError(
      'Request failed with status code 500',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: {},
        config: { headers: {} } as InternalAxiosRequestConfig,
        data: { message: 'Lỗi máy chủ hàng đợi khám (HTTP 500)' },
      },
    );

    const mockApiResponse = buildMockExaminationQueueResponse();
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockRejectedValueOnce(serverError)
      .mockResolvedValueOnce({
        data: {
          ...mockApiResponse,
          facilityName: 'Bệnh viện MedFlow Cơ sở API Live',
        },
      });

    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi máy chủ hàng đợi khám (HTTP 500)')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    await waitFor(() => {
      expect(screen.getByText(/Bệnh viện MedFlow Cơ sở API Live/i)).toBeInTheDocument();
    });
    expect(getSpy).toHaveBeenCalledWith('/reception/examination-queue', {
      params: { queueDate: '2026-11-04' },
    });
  });

  it('renders arrival-class and Safe Backfill badges, filters by arrivalClass, and applies Early Arrival / Safe Backfill workflow', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Phạm Thu Dung')).toBeInTheDocument();
    });

    // Arrival class & Safe Backfill badges on the queue table
    expect(screen.getAllByText('Đúng giờ').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Đến sớm').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Đến muộn').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Vãng lai').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Safe Backfill').length).toBeGreaterThan(0);

    // Filter by arrivalClass = EARLY
    fireEvent.change(screen.getByLabelText('Lọc theo phân loại đến'), {
      target: { value: 'EARLY' },
    });
    expect(screen.getByText('Phạm Thu Dung')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();
    expect(screen.queryByText('Vũ Thị Phương')).not.toBeInTheDocument();

    // Open EarlyArrivalBackfillModal for Phạm Thu Dung (A015)
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Xử lý đến sớm hoặc Safe Backfill cho Phạm Thu Dung',
      }),
    );

    expect(screen.getByText('Xử lý bệnh nhân đến sớm & Safe Backfill (UC28)')).toBeInTheDocument();

    // Select preset reason and submit Safe Backfill
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Tận dụng khoảng trống giữa các ca hẹn để Safe Backfill',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận điều phối' }));

    await waitFor(() => {
      expect(
        screen.getByText(
          /Đã áp dụng Safe Backfill cho phiếu A015 \(Phạm Thu Dung\) tại P\.102 — Thứ tự phục vụ: #3, giữ nguyên số phiếu #15\./i,
        ),
      ).toBeInTheDocument();
    });
    expect(
      screen.getByText('Tận dụng khoảng trống giữa các ca hẹn để Safe Backfill'),
    ).toBeInTheDocument();
  });
});
