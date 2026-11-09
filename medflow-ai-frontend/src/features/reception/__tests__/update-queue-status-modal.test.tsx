import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchQueueEntryStatus, updateQueueEntryStatus } from '../api/queue-status.api';
import { UpdateQueueStatusModal } from '../components/update-queue-status-modal';
import type {
  UpdateQueueStatusResponseDto,
  UpdateQueueStatusTarget,
} from '../types/queue-status-update.types';

const SAMPLE_TARGET: UpdateQueueStatusTarget = {
  entryId: '11',
  queueNumber: 13,
  ticketNo: 'A013',
  visitCode: 'VIS-2026-1002',
  patientName: 'Trần Thị Bích',
  patientCode: 'PAT-00189',
  roomCode: 'P.102',
  roomName: 'Phòng khám Nội tổng quát 1',
  departmentName: 'Khoa Nội tổng quát',
  doctorName: 'BS.CKII. Phạm Quốc Hùng',
  currentStatus: 'CALLED',
  presenceStatus: 'PRESENT',
  priority: 'ELDERLY',
  blockedReason: null,
  stateVersion: 2,
};

function renderModal(props: Partial<Parameters<typeof UpdateQueueStatusModal>[0]> = {}) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onClose = vi.fn();
  const onUpdated = vi.fn();

  const view = render(
    <QueryClientProvider client={qc}>
      <UpdateQueueStatusModal
        open={true}
        target={SAMPLE_TARGET}
        onClose={onClose}
        onUpdated={onUpdated}
        {...props}
      />
    </QueryClientProvider>,
  );

  return { ...view, onClose, onUpdated };
}

describe('UpdateQueueStatusModal & queue-status.api (UC28 - Issue #90)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('calls GET and PATCH /reception/examination-queue/entries/{id}/status in queue-status.api', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 11,
        currentStatus: 'CALLED',
        stateVersion: 2,
        allowedTargetStatuses: ['IN_PROGRESS', 'WAITING', 'SKIPPED'],
        statusHistory: [],
      },
    });

    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({
      data: {
        queueEntryId: 11,
        previousStatus: 'CALLED',
        currentStatus: 'IN_PROGRESS',
        stateVersion: 3,
      },
    });

    await fetchQueueEntryStatus('11');
    expect(getSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/11/status');

    await updateQueueEntryStatus('11', {
      targetStatus: 'IN_PROGRESS',
      presenceStatus: 'PRESENT',
      expectedStateVersion: 2,
    });
    expect(patchSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/11/status', {
      targetStatus: 'IN_PROGRESS',
      presenceStatus: 'PRESENT',
      expectedStateVersion: 2,
    });
  });

  it('renders current entry summary and only displays allowed target transitions for CALLED', () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('offline'));
    renderModal();

    expect(screen.getByText('Cập nhật trạng thái hàng đợi khám (UC28)')).toBeInTheDocument();
    expect(screen.getByText('A013')).toBeInTheDocument();
    expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
    expect(screen.getByText(/PAT-00189 • VIS-2026-1002/)).toBeInTheDocument();

    // Allowed targets from CALLED: IN_PROGRESS, WAITING, SKIPPED
    expect(screen.getByText('Đang khám (IN_PROGRESS)')).toBeInTheDocument();
    expect(screen.getByText('Đang chờ (WAITING)')).toBeInTheDocument();
    expect(screen.getByText('Qua lượt (SKIPPED)')).toBeInTheDocument();
    expect(screen.queryByText('Hoàn tất (COMPLETED)')).not.toBeInTheDocument();
  });

  it('validates required reason when SKIPPED is selected and blocks IN_PROGRESS when patient is LEFT', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('offline'));
    const onSubmitOverride = vi.fn();
    renderModal({ onSubmitOverride });

    // Select SKIPPED without entering a reason
    fireEvent.click(screen.getByText('Qua lượt (SKIPPED)'));
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận cập nhật trạng thái' }));

    expect(
      await screen.findByText('Vui lòng nhập lý do qua lượt (tối thiểu 3 ký tự).'),
    ).toBeInTheDocument();
    expect(onSubmitOverride).not.toHaveBeenCalled();

    // Select IN_PROGRESS, then set presenceStatus to LEFT
    fireEvent.click(screen.getByText('Đang khám (IN_PROGRESS)'));
    fireEvent.click(screen.getByRole('button', { name: /Vắng mặt \(LEFT\)/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận cập nhật trạng thái' }));

    expect(
      await screen.findByText('Không thể bắt đầu khám khi bệnh nhân chưa đến hoặc vắng mặt.'),
    ).toBeInTheDocument();
    expect(onSubmitOverride).not.toHaveBeenCalled();
  });

  it('submits valid transition and surfaces HTTP 409 Conflict error when state_version mismatches', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValue(new Error('offline'));

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

    const mockSuccessResponse = {
      queueEntryId: 11,
      visitId: 1002,
      visitCode: 'VIS-2026-1002',
      ticketNo: 'A013',
      previousStatus: 'CALLED',
      currentStatus: 'IN_PROGRESS',
      presenceStatus: 'PRESENT',
      previousStateVersion: 2,
      stateVersion: 3,
      blockedReason: null,
      reason: 'Bệnh nhân đã vào phòng khám',
    } as unknown as UpdateQueueStatusResponseDto;

    const onSubmitOverride = vi
      .fn()
      .mockRejectedValueOnce(conflictError)
      .mockResolvedValueOnce(mockSuccessResponse);

    const { onUpdated } = renderModal({ onSubmitOverride });

    // First submit -> 409 Conflict displayed
    fireEvent.change(screen.getByLabelText(/Lý do cập nhật/i), {
      target: { value: 'Bệnh nhân đã vào phòng khám' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận cập nhật trạng thái' }));

    expect(
      await screen.findByText(/Dữ liệu hàng đợi đã thay đổi bởi thao tác khác/i),
    ).toBeInTheDocument();
    expect(onUpdated).not.toHaveBeenCalled();

    // Second submit -> succeeds and calls onUpdated
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận cập nhật trạng thái' }));

    await waitFor(() => {
      expect(onUpdated).toHaveBeenCalledWith(
        expect.objectContaining({
          entryId: '11',
          ticketNo: 'A013',
          previousStatus: 'CALLED',
          currentStatus: 'IN_PROGRESS',
        }),
      );
    });
  });
});
