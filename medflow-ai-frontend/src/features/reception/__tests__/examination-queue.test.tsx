import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { updateExaminationQueueStatus } from '../api/examination-queue.api';
import {
  buildMockExaminationQueueResponse,
  buildMockUpdateExaminationQueueStatusResponse,
} from '../data/examination-queue-mock';
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

describe('ExaminationQueuePage (UC28 - Issue #87 & #89)', () => {
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

    // 3. Filter by priority ELDERLY and visitType WALK_IN
    fireEvent.change(screen.getByLabelText('Lọc theo độ ưu tiên'), {
      target: { value: 'ELDERLY' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo phân loại khám'), {
      target: { value: 'WALK_IN' },
    });
    expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();
    expect(screen.queryByText('Trần Thị Bích')).not.toBeInTheDocument();
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
    expect(
      screen.queryByRole('button', { name: /Cập nhật trạng thái hàng đợi cho/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Gọi bệnh nhân tiếp theo tại/i }),
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

  it('opens UpdateQueueStatusModal with valid transitions, transitions CALLED -> IN_PROGRESS and SKIPPED -> WAITING, and disables COMPLETED entries', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
    });

    // COMPLETED entry (Đặng Quốc Bảo - A011) has disabled update button
    const completedBtn = screen.getByRole('button', {
      name: 'Cập nhật trạng thái hàng đợi cho Đặng Quốc Bảo',
    });
    expect(completedBtn).toBeDisabled();

    // Open modal for Trần Thị Bích (A013 - currently CALLED)
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Trần Thị Bích',
      }),
    );

    const dialog = screen.getByRole('dialog');
    expect(
      within(dialog).getByText('Cập nhật trạng thái hàng đợi khám (UC28)'),
    ).toBeInTheDocument();

    // Only valid next transitions for CALLED: IN_PROGRESS, READY, WAITING, SKIPPED
    const radioGroup = within(dialog).getByRole('radiogroup', {
      name: 'Chọn trạng thái hàng đợi mới',
    });
    const radioButtons = within(radioGroup).getAllByRole('radio');
    expect(radioButtons).toHaveLength(4);
    expect(within(radioGroup).getByText('Đang khám')).toBeInTheDocument();
    expect(within(radioGroup).getByText('Sẵn sàng')).toBeInTheDocument();
    expect(within(radioGroup).getByText('Đang chờ')).toBeInTheDocument();
    expect(within(radioGroup).getByText('Qua lượt')).toBeInTheDocument();
    expect(within(radioGroup).queryByText('Hoàn tất')).not.toBeInTheDocument();

    // Submit default first valid transition (IN_PROGRESS - Đang khám)
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(screen.getByRole('status')).toHaveTextContent(
      'Đã cập nhật trạng thái phiếu A013 (Trần Thị Bích) từ Đã gọi số sang Đang khám.',
    );

    // Now transition Ngô Minh Tuấn (B004 - currently SKIPPED) back to WAITING
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Ngô Minh Tuấn',
      }),
    );

    const skippedDialog = screen.getByRole('dialog');
    fireEvent.change(within(skippedDialog).getByLabelText('Trạng thái hiện diện'), {
      target: { value: 'PRESENT' },
    });
    fireEvent.change(
      within(skippedDialog).getByLabelText(/Lý do hoặc ghi chú cập nhật trạng thái/i),
      { target: { value: '' } },
    );
    fireEvent.click(within(skippedDialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Đã cập nhật trạng thái phiếu B004 (Ngô Minh Tuấn) từ Qua lượt sang Đang chờ.',
      );
    });
  });

  it('enforces client Zod validation when skipping without a reason or calling/starting when patient is not present', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Vũ Thị Phương')).toBeInTheDocument();
    });

    // Open modal for Vũ Thị Phương (B006 - currently WAITING)
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Vũ Thị Phương',
      }),
    );

    const dialog = screen.getByRole('dialog');
    const radioGroup = within(dialog).getByRole('radiogroup', {
      name: 'Chọn trạng thái hàng đợi mới',
    });

    // 1. Select CALLED ("Đã gọi số") while changing presence to NOT_ARRIVED ("Chưa đến")
    fireEvent.click(within(radioGroup).getByRole('radio', { name: /Đã gọi số/i }));
    fireEvent.change(within(dialog).getByLabelText('Trạng thái hiện diện'), {
      target: { value: 'NOT_ARRIVED' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    expect(
      within(dialog).getByText(
        'Chỉ có thể gọi số hoặc bắt đầu khám khi bệnh nhân đang có mặt (Có mặt).',
      ),
    ).toBeInTheDocument();

    // 2. Select SKIPPED ("Qua lượt") with empty reason -> validation error
    fireEvent.click(within(radioGroup).getByRole('radio', { name: /Qua lượt/i }));
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    expect(within(dialog).getByText(/Vui lòng nhập lý do qua lượt/i)).toBeInTheDocument();

    // 3. Click quick-fill reason chip and submit -> succeeds
    fireEvent.click(
      within(dialog).getByRole('button', {
        name: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      }),
    );
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('status')).toHaveTextContent(
      'Đã cập nhật trạng thái phiếu B006 (Vũ Thị Phương) từ Đang chờ sang Qua lượt.',
    );
  });

  it('calls PATCH status endpoint when token is present and surfaces HTTP 409 Conflict in modal alert', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

    const mockQueue = buildMockExaminationQueueResponse();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockQueue });

    const conflictError = new AxiosError(
      'Request failed with status code 409',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: { headers: {} } as InternalAxiosRequestConfig,
        data: { message: 'Bệnh nhân đã được bác sĩ khác gọi khám (HTTP 409)' },
      },
    );

    const mockPatchSuccess = buildMockUpdateExaminationQueueStatusResponse(
      'q-002',
      {
        targetStatus: 'IN_PROGRESS',
        presenceStatus: 'PRESENT',
        reason: null,
      },
      mockQueue,
    );

    const patchSpy = vi
      .spyOn(apiClient, 'patch')
      .mockRejectedValueOnce(conflictError)
      .mockResolvedValueOnce({
        data: {
          queueEntryId: 2,
          visitCode: 'VIS-2026-0102',
          ticketNo: 'A013',
          patientName: 'Trần Thị Bích',
          previousStatus: 'CALLED',
          currentStatus: 'IN_PROGRESS',
          presenceStatus: 'PRESENT',
          blockedReason: null,
          reason: null,
        },
      })
      .mockResolvedValueOnce({ data: mockPatchSuccess });

    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
    });

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Trần Thị Bích',
      }),
    );

    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    // First attempt -> HTTP 409 Conflict surfaced in modal alert
    await waitFor(() => {
      expect(within(dialog).getByRole('alert')).toHaveTextContent(
        'Bệnh nhân đã được bác sĩ khác gọi khám (HTTP 409)',
      );
    });
    expect(patchSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-002/status',
      expect.objectContaining({
        targetStatus: 'IN_PROGRESS',
        presenceStatus: 'PRESENT',
      }),
    );

    // Second attempt -> resolves and closes modal
    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('status')).toHaveTextContent(
      'Đã cập nhật trạng thái phiếu A013 (Trần Thị Bích) từ Đã gọi số sang Đang khám.',
    );

    // Direct API helper contract check
    await updateExaminationQueueStatus('q-002', {
      targetStatus: 'IN_PROGRESS',
      presenceStatus: 'PRESENT',
      reason: null,
    });
    expect(patchSpy).toHaveBeenCalledWith('/reception/examination-queue/q-002/status', {
      targetStatus: 'IN_PROGRESS',
      presenceStatus: 'PRESENT',
      reason: null,
    });
  });

  it('disables Update Status action for COMPLETED entries and recalculates KPI & room counters when completing an IN_PROGRESS entry', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Đặng Quốc Bảo')).toBeInTheDocument();
    });

    // COMPLETED entry (Đặng Quốc Bảo - A011) must have disabled "Cập nhật TT" button
    const completedBtn = screen.getByRole('button', {
      name: 'Cập nhật trạng thái hàng đợi cho Đặng Quốc Bảo',
    });
    expect(completedBtn).toBeDisabled();

    // Complete IN_PROGRESS entry (Nguyễn Văn An - A012) -> only COMPLETED transition is available
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Nguyễn Văn An',
      }),
    );

    const dialog = screen.getByRole('dialog');
    const radioGroup = within(dialog).getByRole('radiogroup', {
      name: 'Chọn trạng thái hàng đợi mới',
    });
    const radios = within(radioGroup).getAllByRole('radio');
    expect(radios).toHaveLength(1);
    expect(within(radioGroup).getByText('Hoàn tất')).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('button', { name: /Xác nhận cập nhật/i }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(screen.getByRole('status')).toHaveTextContent(
      'Đã cập nhật trạng thái phiếu A012 (Nguyễn Văn An) từ Đang khám sang Hoàn tất.',
    );

    // Now Nguyễn Văn An's update button should also be disabled
    expect(
      screen.getByRole('button', {
        name: 'Cập nhật trạng thái hàng đợi cho Nguyễn Văn An',
      }),
    ).toBeDisabled();
  });

  it('displays RETURN_CONSULTATION badge, filters by queueType, and disables Apply Priority for IN_PROGRESS entries in mock mode', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Lê Minh Cường')).toBeInTheDocument();
    });

    const cuongRow = screen.getByText('Lê Minh Cường').closest('tr');
    expect(cuongRow).not.toBeNull();
    expect(within(cuongRow!).getByText('Đọc KQ CLS')).toBeInTheDocument();

    const applyPriorityBtn = screen.getByRole('button', {
      name: 'Áp dụng ưu tiên khám ngoại trú cho Lê Minh Cường',
    });
    expect(applyPriorityBtn).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Lọc theo loại lượt khám'), {
      target: { value: 'RETURN_CONSULTATION' },
    });

    expect(screen.getByText('Lê Minh Cường')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();
    expect(screen.queryByText('Vũ Thị Phương')).not.toBeInTheDocument();
  });

  it('applies clinical priority via live API and refetches examination queue with backend servingOrder', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

    const initialList = buildMockExaminationQueueResponse();
    const refetchedList = buildMockExaminationQueueResponse();
    const room103 = refetchedList.servicePoints.find((sp) => sp.servicePointId === 'sp-room-103')!;
    room103.entries = room103.entries.map((entry) =>
      entry.id === 'q-006'
        ? { ...entry, priority: 'ELDERLY', servingOrder: 1 }
        : entry.id === 'q-003'
          ? { ...entry, servingOrder: 2 }
          : entry,
    );

    let listCallCount = 0;
    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url === '/reception/examination-queue') {
        listCallCount += 1;
        return { data: listCallCount === 1 ? initialList : refetchedList };
      }
      if (url === '/reception/examination-queue/entries/q-006/status') {
        return {
          data: {
            queueEntryId: 6,
            currentStatus: 'WAITING',
            stateVersion: 2,
            allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
            statusHistory: [],
          },
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({
      data: {
        queueEntryId: 6,
        visitId: 106,
        visitCode: 'VIS-2026-0106',
        ticketNo: 'B006',
        queueNumber: 6,
        queueType: 'EXAMINATION',
        currentStatus: 'WAITING',
        previousPriority: 'NORMAL',
        priority: 'ELDERLY',
        verificationRequired: true,
        verificationConfirmed: true,
        reason: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945',
        previousStateVersion: 2,
        stateVersion: 3,
        updatedByUserId: 5,
        updatedByStaffCode: 'STF-005',
        updatedByName: 'Lê Tân Tiếp Đón',
        updatedAtUtc: '2026-11-04T02:00:00Z',
      },
    });

    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Vũ Thị Phương')).toBeInTheDocument();
    });

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Áp dụng ưu tiên khám ngoại trú cho Vũ Thị Phương',
      }),
    );

    const dialog = screen.getByRole('dialog');
    await waitFor(() => {
      expect(
        within(dialog).getByRole('button', { name: 'Xác nhận áp dụng ưu tiên' }),
      ).not.toBeDisabled();
    });

    fireEvent.click(within(dialog).getByRole('button', { name: 'Người cao tuổi (ELDERLY)' }));
    fireEvent.change(within(dialog).getByLabelText('Lý do xác minh đối tượng ưu tiên (bắt buộc)'), {
      target: { value: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945' },
    });
    fireEvent.click(
      within(dialog).getByLabelText(/Tôi đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên/i),
    );
    fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận áp dụng ưu tiên' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(patchSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-006/priority', {
      priority: 'ELDERLY',
      reason: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945',
      verificationConfirmed: true,
      expectedStateVersion: 2,
    });

    await waitFor(() => {
      expect(listCallCount).toBe(2);
    });
    expect(getSpy).toHaveBeenCalledWith('/reception/examination-queue', {
      params: { queueDate: '2026-11-04' },
    });

    expect(screen.getByRole('status')).toHaveTextContent(
      /Đã áp dụng mức ưu tiên Người cao tuổi cho phiếu B006/i,
    );

    const phuongRow = screen.getByText('Vũ Thị Phương').closest('tr');
    expect(phuongRow).not.toBeNull();
    expect(within(phuongRow!).getByText('Người cao tuổi')).toBeInTheDocument();
  });

  it('displays LATE appointment-protection loss badge and executes Missed Turn (CALLED -> SKIPPED) & Restore Turn (SKIPPED -> READY) via live API (Issue #97)', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

    const initialList = buildMockExaminationQueueResponse();
    const afterMissedList = buildMockExaminationQueueResponse();
    const room102AfterMissed = afterMissedList.servicePoints.find(
      (sp) => sp.servicePointId === 'sp-room-102',
    )!;
    room102AfterMissed.entries = room102AfterMissed.entries.map((entry) =>
      entry.id === 'q-002'
        ? {
            ...entry,
            status: 'SKIPPED',
            presenceStatus: 'LEFT',
            servingOrder: 4,
            blockedReason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
          }
        : entry,
    );

    const afterRestoreList = buildMockExaminationQueueResponse();
    const room103AfterRestore = afterRestoreList.servicePoints.find(
      (sp) => sp.servicePointId === 'sp-room-103',
    )!;
    room103AfterRestore.entries = room103AfterRestore.entries.map((entry) =>
      entry.id === 'q-008'
        ? {
            ...entry,
            status: 'READY',
            presenceStatus: 'PRESENT',
            arrivalClass: 'LATE',
            isAppointmentProtected: false,
            servingOrder: 2,
            blockedReason: null,
          }
        : entry.id === 'q-006'
          ? { ...entry, servingOrder: 3 }
          : entry,
    );

    let listCallCount = 0;
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url === '/reception/examination-queue') {
        listCallCount += 1;
        if (listCallCount === 1) return { data: initialList };
        if (listCallCount === 2) return { data: afterMissedList };
        return { data: afterRestoreList };
      }
      if (url === '/reception/examination-queue/entries/q-002/status') {
        return {
          data: {
            queueEntryId: 2,
            currentStatus: 'CALLED',
            stateVersion: 2,
            allowedTargetStatuses: ['IN_PROGRESS', 'SKIPPED', 'WAITING'],
            statusHistory: [],
          },
        };
      }
      if (url === '/reception/examination-queue/entries/q-008/status') {
        return {
          data: {
            queueEntryId: 8,
            currentStatus: 'SKIPPED',
            stateVersion: 3,
            allowedTargetStatuses: ['READY', 'WAITING', 'CALLED'],
            statusHistory: [],
          },
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({
        data: {
          queueEntryId: 'q-002',
          ticketNo: 'A013',
          queueNumber: 13,
          visitCode: 'VIS-2026-0102',
          patientCode: 'BN-2024-0042',
          patientName: 'Trần Thị Bích',
          servicePointId: 'sp-room-102',
          action: 'MISSED_TURN',
          previousStatus: 'CALLED',
          currentStatus: 'SKIPPED',
          presenceStatus: 'LEFT',
          arrivalClass: 'ON_TIME_OR_GRACE',
          previousServingOrder: 2,
          servingOrder: 4,
          isAppointmentProtected: false,
          blockedReason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
          reason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
          notes: null,
          previousStateVersion: 2,
          stateVersion: 3,
          updatedAtUtc: '2026-11-04T02:10:00Z',
          updatedEntry: room102AfterMissed.entries.find((e) => e.id === 'q-002')!,
        },
      })
      .mockResolvedValueOnce({
        data: {
          queueEntryId: 'q-008',
          ticketNo: 'B004',
          queueNumber: 4,
          visitCode: 'VIS-2026-0099',
          patientCode: 'BN-2024-0055',
          patientName: 'Ngô Minh Tuấn',
          servicePointId: 'sp-room-103',
          action: 'RESTORE_TURN',
          previousStatus: 'SKIPPED',
          currentStatus: 'READY',
          presenceStatus: 'PRESENT',
          arrivalClass: 'LATE',
          previousServingOrder: 3,
          servingOrder: 2,
          isAppointmentProtected: false,
          blockedReason: null,
          reason: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
          notes: null,
          previousStateVersion: 3,
          stateVersion: 4,
          updatedAtUtc: '2026-11-04T02:15:00Z',
          updatedEntry: room103AfterRestore.entries.find((e) => e.id === 'q-008')!,
        },
      });

    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Ngô Minh Tuấn')).toBeInTheDocument();
    });

    // Scheduled LATE entry (Ngô Minh Tuấn - B004) shows "Đến muộn" and "Mất bảo vệ giờ hẹn"
    const tuanRow = screen.getByText('Ngô Minh Tuấn').closest('tr');
    expect(tuanRow).not.toBeNull();
    expect(within(tuanRow!).getByText('Đến muộn')).toBeInTheDocument();
    expect(within(tuanRow!).getByText('Mất bảo vệ giờ hẹn')).toBeInTheDocument();
    expect(within(tuanRow!).getByText('Qua lượt')).toBeInTheDocument();

    // 1. Mark CALLED entry (Trần Thị Bích - A013) as Missed Turn (CALLED -> SKIPPED)
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Đánh dấu qua lượt khám cho Trần Thị Bích',
      }),
    );

    const missedDialog = screen.getByRole('dialog');
    await waitFor(() => {
      expect(
        within(missedDialog).getByRole('button', { name: 'Xác nhận đánh dấu qua lượt' }),
      ).not.toBeDisabled();
    });

    fireEvent.click(
      within(missedDialog).getByRole('button', {
        name: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      }),
    );
    fireEvent.click(
      within(missedDialog).getByRole('button', { name: 'Xác nhận đánh dấu qua lượt' }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(postSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-002/missed-turn', {
      servicePointId: 'sp-room-102',
      reason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      notes: null,
      expectedStateVersion: 2,
    });
    expect(screen.getByRole('status')).toHaveTextContent(
      /Đã đánh dấu Qua lượt \(SKIPPED\) cho phiếu A013 \(Trần Thị Bích\) — Thứ tự hiển thị: #4, giữ nguyên số phiếu #13\./i,
    );

    // 2. Restore SKIPPED entry (Ngô Minh Tuấn - B004) back to READY
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Khôi phục lượt khám cho Ngô Minh Tuấn',
      }),
    );

    const restoreDialog = screen.getByRole('dialog');
    await waitFor(() => {
      expect(
        within(restoreDialog).getByRole('button', { name: 'Xác nhận khôi phục lượt khám' }),
      ).not.toBeDisabled();
    });

    fireEvent.click(
      within(restoreDialog).getByRole('button', {
        name: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
      }),
    );
    fireEvent.click(
      within(restoreDialog).getByLabelText(/Tôi xác nhận bệnh nhân đã có mặt trực tiếp/i),
    );
    fireEvent.click(
      within(restoreDialog).getByRole('button', { name: 'Xác nhận khôi phục lượt khám' }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(postSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-008/restore-turn',
      {
        servicePointId: 'sp-room-103',
        confirmPresent: true,
        reason: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
        notes: null,
        expectedStateVersion: 3,
      },
    );
    expect(screen.getByRole('status')).toHaveTextContent(
      /Đã khôi phục lượt khám sang Sẵn sàng \(READY\) cho phiếu B004 \(Ngô Minh Tuấn\) — Phân loại: Đến muộn \(mất bảo vệ giờ hẹn cũ\), thứ tự phục vụ mới: #2, giữ nguyên số phiếu #4\./i,
    );
  });

  it('shows Cancel Queue Entry only for WAITING/READY/CALLED/SKIPPED entries (Issue #103)', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Vũ Thị Phương')).toBeInTheDocument();
    });

    for (const name of ['Trần Thị Bích', 'Phạm Thu Dung', 'Vũ Thị Phương', 'Ngô Minh Tuấn']) {
      expect(screen.getByRole('button', { name: `Hủy lượt khám cho ${name}` })).toBeInTheDocument();
    }
    for (const name of ['Nguyễn Văn An', 'Lê Minh Cường', 'Đặng Quốc Bảo']) {
      expect(
        screen.queryByRole('button', { name: `Hủy lượt khám cho ${name}` }),
      ).not.toBeInTheDocument();
    }
  });

  it('cancels a queue entry via live API and shows the backend-refetched queue without it (Issue #103)', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

    const initialList = buildMockExaminationQueueResponse();
    const refetchedList = buildMockExaminationQueueResponse();
    const room103 = refetchedList.servicePoints.find((sp) => sp.servicePointId === 'sp-room-103')!;
    room103.entries = room103.entries
      .filter((entry) => entry.id !== 'q-006')
      .map((entry) => (entry.id === 'q-008' ? { ...entry, servingOrder: 2 } : entry));

    let listCallCount = 0;
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url === '/reception/examination-queue') {
        listCallCount += 1;
        return { data: listCallCount === 1 ? initialList : refetchedList };
      }
      if (url === '/reception/examination-queue/entries/q-006/status') {
        return {
          data: {
            queueEntryId: 6,
            currentStatus: 'WAITING',
            stateVersion: 4,
            allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
            statusHistory: [],
          },
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        queueEntryId: 6,
        ticketNo: 'B006',
        queueNumber: 6,
        previousStatus: 'WAITING',
        currentStatus: 'CANCELLED',
        presenceStatus: 'LEFT',
        previousStateVersion: 4,
        stateVersion: 5,
        allowedTargetStatuses: [],
        statusHistory: [],
      },
    });

    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Vũ Thị Phương')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Hủy lượt khám cho Vũ Thị Phương' }));

    const dialog = screen.getByRole('dialog');
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Xác nhận hủy lượt' })).not.toBeDisabled();
    });
    fireEvent.change(within(dialog).getByLabelText('Lý do hủy lượt (bắt buộc)'), {
      target: { value: 'Bệnh nhân xin về, không tiếp tục khám' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận hủy lượt' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(postSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-006/cancel', {
      reason: 'Bệnh nhân xin về, không tiếp tục khám',
      expectedStateVersion: 4,
      notes: null,
    });

    await waitFor(() => {
      expect(screen.queryByText('Vũ Thị Phương')).not.toBeInTheDocument();
    });
    expect(listCallCount).toBe(2);
    expect(screen.getByRole('status')).toHaveTextContent(
      /Đã hủy lượt khám phiếu B006 \(Vũ Thị Phương\) tại P\.103 — giữ nguyên số phiếu #6/i,
    );

    const tuanRow = screen.getByText('Ngô Minh Tuấn').closest('tr');
    expect(tuanRow).not.toBeNull();
    expect(within(tuanRow!).getByText('#2')).toBeInTheDocument();
  });

  it('calls POST /queue/service-points/sp-room-104/call-next from Reception Queue, shows selection reason, and updates service point entries (Issue #93 / UC31)', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
    });

    const calledEntry = {
      id: 'q-005',
      queueNumber: 1,
      ticketNo: 'C001',
      servingOrder: 1,
      visitCode: 'VIS-2026-0105',
      patientName: 'Hoàng Đức Em',
      patientCode: 'BN-2024-0103',
      visitType: 'WALK_IN' as const,
      servicePointId: 'sp-room-104',
      roomCode: 'P.104',
      roomName: 'Phòng khám Tai Mũi Họng',
      departmentName: 'Khoa Tai Mũi Họng',
      doctorName: 'BS.CKI Đinh Long',
      status: 'CALLED' as const,
      priority: 'ELDERLY' as const,
      queueType: 'EXAMINATION' as const,
      presenceStatus: 'PRESENT' as const,
      arrivalClass: 'WALK_IN' as const,
      scheduledAt: null,
      checkedInAt: '08:40',
      calledAt: '08:42',
      startedAt: null,
      blockedReason: null,
      queueGroup: 'CALLED' as const,
      ineligibleReason: null,
    };

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        servicePointId: 'sp-room-104',
        outcome: 'CALLED',
        message: 'Đã gọi bệnh nhân Hoàng Đức Em (phiếu C001) vào phòng P.104.',
        selectionReason: 'PRIORITY',
        calledEntry,
        previousStateVersion: 1,
        stateVersion: 2,
        updatedAtUtc: '2026-11-04T01:42:00Z',
        entries: [calledEntry],
      },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Gọi bệnh nhân tiếp theo tại P.104' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/queue/service-points/sp-room-104/call-next', {});
    });

    const statusBanner = await screen.findByRole('status');
    expect(statusBanner).toHaveTextContent(
      'Đã gọi bệnh nhân Hoàng Đức Em (phiếu C001) vào phòng P.104.',
    );
    expect(statusBanner).toHaveTextContent('Lý do chọn: Ưu tiên');
    expect(statusBanner).toHaveTextContent('C001');

    const patientRow = screen
      .getAllByText('Hoàng Đức Em')
      .map((el) => el.closest('tr'))
      .find((row) => row !== null);
    expect(patientRow).toBeDefined();
    expect(within(patientRow!).getByText('Đã gọi số')).toBeInTheDocument();
  });

  it('handles HOLD_FOR_SLOT, NO_ELIGIBLE_PATIENT, and 409 Conflict responses on /reception/queue (Issue #93 / UC31)', async () => {
    renderQueuePage();

    await waitFor(() => {
      expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
    });

    const initialList = buildMockExaminationQueueResponse();
    const room104Entries = initialList.servicePoints.find(
      (sp) => sp.servicePointId === 'sp-room-104',
    )!.entries;

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          'Điểm dịch vụ P.102 đang có bệnh nhân đang khám (A012). Không thể gọi bệnh nhân tiếp theo.',
      },
    });

    vi.spyOn(apiClient, 'post')
      .mockResolvedValueOnce({
        data: {
          servicePointId: 'sp-room-104',
          outcome: 'HOLD_FOR_SLOT',
          message: 'Giữ khoảng trống bảo vệ lịch hẹn sắp tới tại phòng P.104.',
          selectionReason: null,
          calledEntry: null,
          previousStateVersion: null,
          stateVersion: null,
          updatedAtUtc: '2026-11-04T01:43:00Z',
          entries: room104Entries,
        },
      })
      .mockResolvedValueOnce({
        data: {
          servicePointId: 'sp-room-104',
          outcome: 'NO_ELIGIBLE_PATIENT',
          message: 'Chưa có bệnh nhân đủ điều kiện gọi tại phòng P.104.',
          selectionReason: null,
          calledEntry: null,
          previousStateVersion: null,
          stateVersion: null,
          updatedAtUtc: '2026-11-04T01:44:00Z',
          entries: room104Entries,
        },
      })
      .mockRejectedValueOnce(conflictError);

    fireEvent.click(screen.getByRole('button', { name: 'Gọi bệnh nhân tiếp theo tại P.104' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Giữ khoảng trống bảo vệ lịch hẹn sắp tới tại phòng P.104.',
      );
    });

    fireEvent.click(screen.getByRole('button', { name: 'Gọi bệnh nhân tiếp theo tại P.104' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Chưa có bệnh nhân đủ điều kiện gọi tại phòng P.104.',
      );
    });

    fireEvent.click(screen.getByRole('button', { name: 'Gọi bệnh nhân tiếp theo tại P.102' }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Điểm dịch vụ P.102 đang có bệnh nhân đang khám (A012). Không thể gọi bệnh nhân tiếp theo.',
      );
    });
  });
});
