import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { markQueueEntryMissedTurn, restoreSkippedQueueTurn } from '../api/examination-queue.api';
import { LateArrivalMissedTurnModal } from '../components/late-arrival-missed-turn-modal';
import type {
  ExaminationQueueEntryDto,
  LateArrivalMissedTurnTarget,
  QueueTurnActionResponseDto,
  ServicePointQueueDto,
} from '../types/examination-queue.types';

const SAMPLE_SERVICE_POINT: ServicePointQueueDto = {
  servicePointId: 'sp-room-103',
  servicePointName: 'Điểm khám Tim mạch 1',
  roomCode: 'P.103',
  roomName: 'Phòng khám Tim mạch 1',
  departmentName: 'Khoa Tim mạch',
  doctorName: 'BS.CKI Lê Hoàng Nam',
  isActive: true,
  currentServingTicketNo: 'B005',
  nextTicketNo: 'B006',
  waitingCount: 1,
  inProgressCount: 1,
  completedCount: 0,
  skippedCount: 1,
  entries: [],
};

const CALLED_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-002',
  queueNumber: 13,
  ticketNo: 'A013',
  servingOrder: 2,
  visitCode: 'VIS-2026-0102',
  patientName: 'Trần Thị Bích',
  patientCode: 'BN-2024-0042',
  visitType: 'SCHEDULED',
  servicePointId: 'sp-room-102',
  roomCode: 'P.102',
  roomName: 'Phòng khám Nội tổng quát 1',
  departmentName: 'Khoa Nội tổng quát',
  doctorName: 'BS.CKII Trần Khánh',
  status: 'CALLED',
  priority: 'ELDERLY',
  queueType: 'EXAMINATION',
  presenceStatus: 'PRESENT',
  arrivalClass: 'ON_TIME_OR_GRACE',
  scheduledAt: '08:15',
  checkedInAt: '08:12',
  calledAt: '08:18',
  startedAt: null,
  blockedReason: null,
  isAppointmentProtected: true,
};

const SKIPPED_LATE_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-008',
  queueNumber: 4,
  ticketNo: 'B004',
  servingOrder: 3,
  visitCode: 'VIS-2026-0099',
  patientName: 'Ngô Minh Tuấn',
  patientCode: 'BN-2024-0055',
  visitType: 'SCHEDULED',
  servicePointId: 'sp-room-103',
  roomCode: 'P.103',
  roomName: 'Phòng khám Tim mạch 1',
  departmentName: 'Khoa Tim mạch',
  doctorName: 'BS.CKI Lê Hoàng Nam',
  status: 'SKIPPED',
  priority: 'NORMAL',
  queueType: 'EXAMINATION',
  presenceStatus: 'LEFT',
  arrivalClass: 'LATE',
  scheduledAt: '08:00',
  checkedInAt: '08:19',
  calledAt: '08:20',
  startedAt: null,
  blockedReason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
  isAppointmentProtected: false,
};

function renderModal(target: LateArrivalMissedTurnTarget) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onClose = vi.fn();
  const onApplied = vi.fn();

  const view = render(
    <QueryClientProvider client={qc}>
      <LateArrivalMissedTurnModal
        open={true}
        target={target}
        onClose={onClose}
        onApplied={onApplied}
      />
    </QueryClientProvider>,
  );

  return { ...view, onClose, onApplied };
}

describe('LateArrivalMissedTurnModal & API helpers (UC28 - Issue #97)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('calls POST /reception/examination-queue/entries/{id}/missed-turn and /restore-turn directly', async () => {
    const mockMissedResponse: QueueTurnActionResponseDto = {
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
      updatedEntry: {
        ...CALLED_ENTRY,
        status: 'SKIPPED',
        presenceStatus: 'LEFT',
        servingOrder: 4,
        blockedReason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      },
    };

    const mockRestoreResponse: QueueTurnActionResponseDto = {
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
      notes: 'Đã kiểm tra thẻ khám tại quầy',
      previousStateVersion: 3,
      stateVersion: 4,
      updatedAtUtc: '2026-11-04T02:15:00Z',
      updatedEntry: {
        ...SKIPPED_LATE_ENTRY,
        status: 'READY',
        presenceStatus: 'PRESENT',
        servingOrder: 2,
        blockedReason: null,
      },
    };

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({ data: mockMissedResponse })
      .mockResolvedValueOnce({ data: mockRestoreResponse });

    const missedResult = await markQueueEntryMissedTurn('q-002', {
      servicePointId: 'sp-room-102',
      reason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      notes: null,
      expectedStateVersion: 2,
    });

    expect(postSpy).toHaveBeenCalledWith('/reception/examination-queue/entries/q-002/missed-turn', {
      servicePointId: 'sp-room-102',
      reason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      notes: null,
      expectedStateVersion: 2,
    });
    expect(missedResult).toEqual(mockMissedResponse);

    const restoreResult = await restoreSkippedQueueTurn('q-008', {
      servicePointId: 'sp-room-103',
      confirmPresent: true,
      reason: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
      notes: 'Đã kiểm tra thẻ khám tại quầy',
      expectedStateVersion: 3,
    });

    expect(postSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-008/restore-turn',
      {
        servicePointId: 'sp-room-103',
        confirmPresent: true,
        reason: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
        notes: 'Đã kiểm tra thẻ khám tại quầy',
        expectedStateVersion: 3,
      },
    );
    expect(restoreResult).toEqual(mockRestoreResponse);
  });

  it('validates mandatory reason and confirmPresent checkbox in RESTORE_TURN mode before calling live API', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 8,
        currentStatus: 'SKIPPED',
        stateVersion: 3,
        allowedTargetStatuses: ['READY', 'WAITING', 'CALLED'],
        statusHistory: [],
      },
    });

    const mockResponse: QueueTurnActionResponseDto = {
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
      updatedEntry: {
        ...SKIPPED_LATE_ENTRY,
        status: 'READY',
        presenceStatus: 'PRESENT',
        servingOrder: 2,
        blockedReason: null,
      },
    };

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockResponse });

    const { onApplied } = renderModal({
      entry: SKIPPED_LATE_ENTRY,
      servicePoint: SAMPLE_SERVICE_POINT,
      mode: 'RESTORE_TURN',
    });

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Xác nhận khôi phục lượt khám' }),
      ).not.toBeDisabled();
    });

    // Late scheduled badge & previous blocked reason are displayed
    expect(screen.getByText('Mất bảo vệ giờ hẹn')).toBeInTheDocument();
    expect(screen.getByText('Giữ nguyên số phiếu #4')).toBeInTheDocument();
    expect(
      screen.getByText(/Lý do qua lượt trước đó: Gọi 3 lần chưa có mặt tại cửa phòng khám/i),
    ).toBeInTheDocument();

    // Submit empty form -> shows reason & confirmPresent validation errors
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận khôi phục lượt khám' }));

    expect(screen.getByText('Lý do điều phối phải từ 5 đến 300 ký tự.')).toBeInTheDocument();
    expect(
      screen.getByText('Cần xác nhận bệnh nhân đã có mặt (PRESENT) trước khi khôi phục lượt khám.'),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // Select preset reason & tick confirmPresent checkbox
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
      }),
    );
    fireEvent.click(screen.getByLabelText(/Tôi xác nhận bệnh nhân đã có mặt trực tiếp/i));
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận khôi phục lượt khám' }));

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith(mockResponse);
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
  });

  it('surfaces HTTP 409 Conflict / 500 error in alert, refetches stateVersion on 409, and never falls back to mock', async () => {
    let statusGetCalls = 0;
    vi.spyOn(apiClient, 'get').mockImplementation(async () => {
      statusGetCalls += 1;
      return {
        data: {
          queueEntryId: 2,
          currentStatus: 'CALLED',
          stateVersion: statusGetCalls === 1 ? 2 : 3,
          allowedTargetStatuses: ['IN_PROGRESS', 'SKIPPED', 'WAITING'],
          statusHistory: [],
        },
      };
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
          message:
            'Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.',
        },
      },
    );

    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    const { onApplied } = renderModal({
      entry: CALLED_ENTRY,
      servicePoint: {
        ...SAMPLE_SERVICE_POINT,
        servicePointId: 'sp-room-102',
        roomCode: 'P.102',
        roomName: 'Phòng khám Nội tổng quát 1',
      },
      mode: 'MISSED_TURN',
    });

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Xác nhận đánh dấu qua lượt' })).not.toBeDisabled();
    });

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đánh dấu qua lượt' }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.',
      );
    });

    expect(postSpy).toHaveBeenCalledTimes(1);
    expect(onApplied).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(statusGetCalls).toBe(2);
    });
  });
});
