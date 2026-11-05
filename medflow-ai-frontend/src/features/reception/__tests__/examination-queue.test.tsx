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
      screen.queryByRole('button', { name: /Cập nhật trạng thái hàng đợi cho/i }),
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
});
