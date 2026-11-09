import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { updateQueueEntryPriority } from '../api/queue-status.api';
import { ApplyClinicalPriorityModal } from '../components/apply-clinical-priority-modal';
import type { ExaminationQueueEntryDto } from '../types/examination-queue.types';
import type { UpdateQueuePriorityResponseDto } from '../types/queue-status-update.types';

const SAMPLE_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-005',
  queueNumber: 1,
  ticketNo: 'C001',
  servingOrder: 1,
  visitCode: 'VIS-2026-0105',
  patientName: 'Hoàng Đức Em',
  patientCode: 'BN-2024-0103',
  visitType: 'WALK_IN',
  servicePointId: 'sp-room-104',
  roomCode: 'P.104',
  roomName: 'Phòng khám Tai Mũi Họng',
  departmentName: 'Khoa Tai Mũi Họng',
  doctorName: 'BS.CKI Đinh Long',
  status: 'WAITING',
  priority: 'NORMAL',
  queueType: 'EXAMINATION',
  presenceStatus: 'PRESENT',
  arrivalClass: 'WALK_IN',
  scheduledAt: null,
  checkedInAt: '08:40',
  calledAt: null,
  startedAt: null,
  blockedReason: null,
};

function renderModal(props: Partial<Parameters<typeof ApplyClinicalPriorityModal>[0]> = {}) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onClose = vi.fn();
  const onApplied = vi.fn();

  const view = render(
    <QueryClientProvider client={qc}>
      <ApplyClinicalPriorityModal
        open={true}
        entry={SAMPLE_ENTRY}
        onClose={onClose}
        onApplied={onApplied}
        {...props}
      />
    </QueryClientProvider>,
  );

  return { ...view, onClose, onApplied };
}

describe('ApplyClinicalPriorityModal & updateQueueEntryPriority (UC28 - Issue #101)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('calls PATCH /reception/examination-queue/entries/{id}/priority in updateQueueEntryPriority', async () => {
    const mockResponse: UpdateQueuePriorityResponseDto = {
      queueEntryId: 5,
      visitId: 105,
      visitCode: 'VIS-2026-0105',
      ticketNo: 'C001',
      queueNumber: 1,
      queueType: 'EXAMINATION',
      currentStatus: 'WAITING',
      previousPriority: 'NORMAL',
      priority: 'ELDERLY',
      verificationRequired: true,
      verificationConfirmed: true,
      reason: 'Đã kiểm tra CCCD bệnh nhân 78 tuổi',
      previousStateVersion: 4,
      stateVersion: 5,
      updatedByUserId: 5,
      updatedByStaffCode: 'STF-005',
      updatedByName: 'Lê Tân Tiếp Đón',
      updatedAtUtc: '2026-11-04T02:00:00Z',
    };

    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({ data: mockResponse });

    const result = await updateQueueEntryPriority('q-005', {
      priority: 'ELDERLY',
      reason: 'Đã kiểm tra CCCD bệnh nhân 78 tuổi',
      verificationConfirmed: true,
      expectedStateVersion: 4,
    });

    expect(patchSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-005/priority', {
      priority: 'ELDERLY',
      reason: 'Đã kiểm tra CCCD bệnh nhân 78 tuổi',
      verificationConfirmed: true,
      expectedStateVersion: 4,
    });
    expect(result).toEqual(mockResponse);
  });

  it('renders only 3 outpatient priority options and excludes URGENT / RETURN_RESULT', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 5,
        currentStatus: 'WAITING',
        stateVersion: 4,
        allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
        statusHistory: [],
      },
    });

    renderModal();

    expect(screen.getByText('Áp dụng ưu tiên khám ngoại trú')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bình thường (NORMAL)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Người cao tuổi (ELDERLY)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Thai phụ (PREGNANT)' })).toBeInTheDocument();

    expect(screen.queryByText(/URGENT|Khẩn cấp|Cấp cứu|RETURN_RESULT/)).toBeNull();
  });

  it('validates mandatory reason and verification checkbox before submitting PATCH with expectedStateVersion', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 5,
        currentStatus: 'WAITING',
        stateVersion: 4,
        allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
        statusHistory: [],
      },
    });

    const mockResponse: UpdateQueuePriorityResponseDto = {
      queueEntryId: 5,
      visitId: 105,
      visitCode: 'VIS-2026-0105',
      ticketNo: 'C001',
      queueNumber: 1,
      queueType: 'EXAMINATION',
      currentStatus: 'WAITING',
      previousPriority: 'NORMAL',
      priority: 'ELDERLY',
      verificationRequired: true,
      verificationConfirmed: true,
      reason: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945',
      previousStateVersion: 4,
      stateVersion: 5,
      updatedByUserId: 5,
      updatedByStaffCode: 'STF-005',
      updatedByName: 'Lê Tân Tiếp Đón',
      updatedAtUtc: '2026-11-04T02:00:00Z',
    };
    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({ data: mockResponse });

    const { onApplied } = renderModal();

    const submitBtn = screen.getByRole('button', { name: 'Xác nhận áp dụng ưu tiên' });
    await waitFor(() => {
      expect(submitBtn).not.toBeDisabled();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Người cao tuổi (ELDERLY)' }));
    fireEvent.click(submitBtn);

    expect(
      await screen.findByText('Vui lòng nhập lý do xác minh đối tượng ưu tiên.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Vui lòng xác nhận đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên.'),
    ).toBeInTheDocument();
    expect(patchSpy).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Lý do xác minh đối tượng ưu tiên (bắt buộc)'), {
      target: { value: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945' },
    });
    fireEvent.click(submitBtn);
    expect(
      await screen.findByText(
        'Vui lòng xác nhận đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên.',
      ),
    ).toBeInTheDocument();
    expect(patchSpy).not.toHaveBeenCalled();

    fireEvent.click(screen.getByLabelText(/Tôi đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên/i));
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(patchSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-005/priority', {
        priority: 'ELDERLY',
        reason: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945',
        verificationConfirmed: true,
        expectedStateVersion: 4,
      });
    });
    expect(onApplied).toHaveBeenCalledWith(mockResponse);
  });

  it('surfaces backend HTTP 409 state_version mismatch in alert without faking local success', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        queueEntryId: 5,
        currentStatus: 'WAITING',
        stateVersion: 4,
        allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
        statusHistory: [],
      },
    });

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
        data: {
          detail:
            'Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.',
        },
      },
    );
    vi.spyOn(apiClient, 'patch').mockRejectedValueOnce(conflictError);

    const { onApplied } = renderModal();

    const submitBtn = screen.getByRole('button', { name: 'Xác nhận áp dụng ưu tiên' });
    await waitFor(() => {
      expect(submitBtn).not.toBeDisabled();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Người cao tuổi (ELDERLY)' }));
    fireEvent.change(screen.getByLabelText('Lý do xác minh đối tượng ưu tiên (bắt buộc)'), {
      target: { value: 'Đã kiểm tra CCCD, bệnh nhân sinh năm 1945' },
    });
    fireEvent.click(screen.getByLabelText(/Tôi đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên/i));
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.',
      );
    });
    expect(onApplied).not.toHaveBeenCalled();
  });

  it('disables submit button and shows error alert when GET status detail fails', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Mất kết nối máy chủ'));

    renderModal();

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        /Không tải được phiên bản trạng thái hiện tại của lượt xếp hàng/i,
      );
    });

    expect(screen.getByRole('button', { name: 'Xác nhận áp dụng ưu tiên' })).toBeDisabled();
  });
});
