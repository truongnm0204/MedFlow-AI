import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { createReturnToDoctorQueueEntry } from '../api/examination-queue.api';
import { ReturnToDoctorQueueModal } from '../components/return-to-doctor-queue-modal';
import type {
  CreateReturnToDoctorQueueEntryResponseDto,
  ExaminationQueueEntryDto,
  ReturnToDoctorQueueTarget,
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

const COMPLETED_EXAMINATION_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-007',
  queueNumber: 11,
  ticketNo: 'A011',
  servingOrder: 4,
  visitCode: 'VIS-2026-0098',
  patientName: 'Đặng Quốc Bảo',
  patientCode: 'BN-2024-0011',
  visitType: 'SCHEDULED',
  servicePointId: 'sp-room-102',
  roomCode: 'P.102',
  roomName: 'Phòng khám Nội tổng quát 1',
  departmentName: 'Khoa Nội tổng quát',
  doctorName: 'BS.CKII Trần Khánh',
  status: 'COMPLETED',
  priority: 'NORMAL',
  queueType: 'EXAMINATION',
  presenceStatus: 'PRESENT',
  arrivalClass: 'ON_TIME_OR_GRACE',
  scheduledAt: '07:30',
  checkedInAt: '07:28',
  calledAt: '07:35',
  startedAt: '07:37',
  blockedReason: null,
};

const WAITING_RETURN_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-009',
  queueNumber: 7,
  ticketNo: 'B007',
  servingOrder: 3,
  visitCode: 'VIS-2026-0106',
  patientName: 'Vũ Thị Phương',
  patientCode: 'BN-2024-0117',
  visitType: 'SCHEDULED',
  servicePointId: 'sp-room-103',
  roomCode: 'P.103',
  roomName: 'Phòng khám Tim mạch 1',
  departmentName: 'Khoa Tim mạch',
  doctorName: 'BS.CKI Lê Hoàng Nam',
  status: 'WAITING',
  priority: 'NORMAL',
  queueType: 'RETURN_CONSULTATION',
  presenceStatus: 'PRESENT',
  arrivalClass: 'ON_TIME_OR_GRACE',
  scheduledAt: '08:45',
  checkedInAt: '09:10',
  calledAt: null,
  startedAt: null,
  blockedReason: null,
  isEligible: false,
  queueGroup: 'NOT_ELIGIBLE',
  ineligibleReason: 'AWAITING_RESULTS',
};

function renderModal(target: ReturnToDoctorQueueTarget) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onClose = vi.fn();
  const onApplied = vi.fn();

  const view = render(
    <QueryClientProvider client={qc}>
      <ReturnToDoctorQueueModal
        open={true}
        target={target}
        onClose={onClose}
        onApplied={onApplied}
      />
    </QueryClientProvider>,
  );

  return { ...view, onClose, onApplied };
}

describe('ReturnToDoctorQueueModal & API helpers (UC28 - Issue #99)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('calls POST /reception/examination-queue/entries/{id}/return-to-doctor directly', async () => {
    const mockResponse: CreateReturnToDoctorQueueEntryResponseDto = {
      queueEntryId: 'q-010',
      ticketNo: 'A016',
      queueNumber: 16,
      visitId: 98,
      visitCode: 'VIS-2026-0098',
      patientCode: 'BN-2024-0011',
      patientName: 'Đặng Quốc Bảo',
      servicePointId: 'sp-room-102',
      roomCode: 'P.102',
      roomName: 'Phòng khám Nội tổng quát 1',
      actionOutcome: 'CREATED',
      previousQueueEntryId: 'q-007',
      previousStatus: 'COMPLETED',
      currentStatus: 'READY',
      queueType: 'RETURN_CONSULTATION',
      priority: 'NORMAL',
      presenceStatus: 'PRESENT',
      arrivalClass: 'ON_TIME_OR_GRACE',
      finalResultRequired: true,
      allDiagnosticResultsReady: true,
      openDiagnosticCount: 0,
      isEligible: true,
      queueGroup: 'ELIGIBLE',
      ineligibleReason: null,
      canSafeBackfill: false,
      isAppointmentProtected: false,
      holdReason: null,
      previousServingOrder: 4,
      servingOrder: 3,
      reason: 'Bệnh nhân đã hoàn tất cận lâm sàng và quay lại phòng khám đọc kết quả',
      notes: null,
      previousStateVersion: 2,
      stateVersion: 1,
      updatedAtUtc: '2026-11-04T02:30:00Z',
      updatedEntry: {
        ...COMPLETED_EXAMINATION_ENTRY,
        id: 'q-010',
        queueNumber: 16,
        ticketNo: 'A016',
        servingOrder: 3,
        status: 'READY',
        queueType: 'RETURN_CONSULTATION',
        isEligible: true,
        queueGroup: 'ELIGIBLE',
        ineligibleReason: null,
      },
    };

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockResponse });

    const result = await createReturnToDoctorQueueEntry('q-007', {
      servicePointId: 'sp-room-102',
      confirmPresent: true,
      diagnosticResultsReady: true,
      reason: 'Bệnh nhân đã hoàn tất cận lâm sàng và quay lại phòng khám đọc kết quả',
      notes: null,
      expectedStateVersion: 2,
    });

    expect(postSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-007/return-to-doctor',
      {
        servicePointId: 'sp-room-102',
        confirmPresent: true,
        diagnosticResultsReady: true,
        reason: 'Bệnh nhân đã hoàn tất cận lâm sàng và quay lại phòng khám đọc kết quả',
        notes: null,
        expectedStateVersion: 2,
      },
    );
    expect(result).toEqual(mockResponse);
  });

  it('validates CREATE_RETURN form inputs, updates live outcome preview, and submits awaiting-results WAITING entry', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 7,
        currentStatus: 'COMPLETED',
        stateVersion: 2,
        allowedTargetStatuses: [],
        statusHistory: [],
      },
    });

    const mockCreatedWaitingResponse: CreateReturnToDoctorQueueEntryResponseDto = {
      queueEntryId: 'q-010',
      ticketNo: 'A016',
      queueNumber: 16,
      visitId: 98,
      visitCode: 'VIS-2026-0098',
      patientCode: 'BN-2024-0011',
      patientName: 'Đặng Quốc Bảo',
      servicePointId: 'sp-room-102',
      roomCode: 'P.102',
      roomName: 'Phòng khám Nội tổng quát 1',
      actionOutcome: 'CREATED',
      previousQueueEntryId: 'q-007',
      previousStatus: 'COMPLETED',
      currentStatus: 'WAITING',
      queueType: 'RETURN_CONSULTATION',
      priority: 'NORMAL',
      presenceStatus: 'PRESENT',
      arrivalClass: 'ON_TIME_OR_GRACE',
      finalResultRequired: true,
      allDiagnosticResultsReady: false,
      openDiagnosticCount: 1,
      isEligible: false,
      queueGroup: 'NOT_ELIGIBLE',
      ineligibleReason: 'AWAITING_RESULTS',
      canSafeBackfill: false,
      isAppointmentProtected: false,
      holdReason: null,
      previousServingOrder: 4,
      servingOrder: 4,
      reason: 'Đăng ký lượt quay lại phòng khám bác sĩ trong khi chờ trả kết quả cận lâm sàng',
      notes: 'Chờ kết quả siêu âm tim',
      previousStateVersion: 2,
      stateVersion: 1,
      updatedAtUtc: '2026-11-04T02:32:00Z',
      updatedEntry: {
        ...COMPLETED_EXAMINATION_ENTRY,
        id: 'q-010',
        queueNumber: 16,
        ticketNo: 'A016',
        servingOrder: 4,
        status: 'WAITING',
        queueType: 'RETURN_CONSULTATION',
        isEligible: false,
        queueGroup: 'NOT_ELIGIBLE',
        ineligibleReason: 'AWAITING_RESULTS',
      },
    };

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({ data: mockCreatedWaitingResponse });

    const { onApplied } = renderModal({
      entry: COMPLETED_EXAMINATION_ENTRY,
      servicePoint: {
        ...SAMPLE_SERVICE_POINT,
        servicePointId: 'sp-room-102',
        roomCode: 'P.102',
        roomName: 'Phòng khám Nội tổng quát 1',
        doctorName: 'BS.CKII Trần Khánh',
      },
      mode: 'CREATE_RETURN',
    });

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Xác nhận tiếp nhận lượt khám lại' }),
      ).not.toBeDisabled();
    });

    expect(
      screen.getByText('Tiếp nhận bệnh nhân quay lại đọc kết quả CLS (RETURN_CONSULTATION)'),
    ).toBeInTheDocument();
    expect(screen.getByText('Ưu tiên: Bình thường')).toBeInTheDocument();
    expect(screen.getByTestId('return-to-doctor-outcome-preview')).toHaveTextContent(
      'Trạng thái sau lưu: Sẵn sàng (READY) • Đủ điều kiện xếp thứ tự gọi khám',
    );

    // 1. Validate short reason (< 5 chars)
    const reasonInput = screen.getByLabelText('Lý do điều phối lượt khám lại (bắt buộc)');
    fireEvent.change(reasonInput, { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận tiếp nhận lượt khám lại' }));
    expect(screen.getByText('Lý do điều phối phải từ 5 đến 300 ký tự.')).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // 2. Validate < or > characters
    fireEvent.change(reasonInput, { target: { value: 'Lý do <script> không hợp lệ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận tiếp nhận lượt khám lại' }));
    expect(screen.getByText('Lý do điều phối không được chứa ký tự < hoặc >.')).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // 3. Uncheck diagnosticResultsReady -> preview updates to WAITING
    fireEvent.click(
      screen.getByLabelText('Đã có đầy đủ kết quả cận lâm sàng bắt buộc (final_result_required)'),
    );
    expect(screen.getByTestId('return-to-doctor-outcome-preview')).toHaveTextContent(
      'Trạng thái sau lưu: Đang chờ (WAITING) • Chưa đủ điều kiện gọi (Chờ KQ CLS / Chưa có mặt)',
    );

    // 4. Select preset reason and notes, then submit
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Đăng ký lượt quay lại phòng khám bác sĩ trong khi chờ trả kết quả cận lâm sàng',
      }),
    );
    fireEvent.change(screen.getByLabelText('Ghi chú bổ sung (tùy chọn)'), {
      target: { value: 'Chờ kết quả siêu âm tim' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận tiếp nhận lượt khám lại' }));

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith(mockCreatedWaitingResponse);
    });
    expect(postSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-007/return-to-doctor',
      {
        servicePointId: 'sp-room-102',
        confirmPresent: true,
        diagnosticResultsReady: false,
        reason: 'Đăng ký lượt quay lại phòng khám bác sĩ trong khi chờ trả kết quả cận lâm sàng',
        notes: 'Chờ kết quả siêu âm tim',
        expectedStateVersion: 2,
      },
    );
  });

  it('enforces mandatory diagnosticResultsReady and confirmPresent checkboxes in ACTIVATE_RETURN mode before calling POST', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        queueEntryId: 9,
        currentStatus: 'WAITING',
        stateVersion: 3,
        allowedTargetStatuses: ['READY'],
        statusHistory: [],
      },
    });

    const mockActivatedResponse: CreateReturnToDoctorQueueEntryResponseDto = {
      queueEntryId: 'q-009',
      ticketNo: 'B007',
      queueNumber: 7,
      visitId: 106,
      visitCode: 'VIS-2026-0106',
      patientCode: 'BN-2024-0117',
      patientName: 'Vũ Thị Phương',
      servicePointId: 'sp-room-103',
      roomCode: 'P.103',
      roomName: 'Phòng khám Tim mạch 1',
      actionOutcome: 'ACTIVATED',
      previousQueueEntryId: 'q-009',
      previousStatus: 'WAITING',
      currentStatus: 'READY',
      queueType: 'RETURN_CONSULTATION',
      priority: 'NORMAL',
      presenceStatus: 'PRESENT',
      arrivalClass: 'ON_TIME_OR_GRACE',
      finalResultRequired: true,
      allDiagnosticResultsReady: true,
      openDiagnosticCount: 0,
      isEligible: true,
      queueGroup: 'ELIGIBLE',
      ineligibleReason: null,
      canSafeBackfill: false,
      isAppointmentProtected: false,
      holdReason: null,
      previousServingOrder: 3,
      servingOrder: 2,
      reason: 'Đã có đầy đủ kết quả cận lâm sàng bắt buộc, bệnh nhân có mặt sẵn sàng đọc kết quả',
      notes: null,
      previousStateVersion: 3,
      stateVersion: 4,
      updatedAtUtc: '2026-11-04T02:40:00Z',
      updatedEntry: {
        ...WAITING_RETURN_ENTRY,
        status: 'READY',
        servingOrder: 2,
        isEligible: true,
        queueGroup: 'ELIGIBLE',
        ineligibleReason: null,
      },
    };

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({ data: mockActivatedResponse });

    const { onApplied } = renderModal({
      entry: WAITING_RETURN_ENTRY,
      servicePoint: SAMPLE_SERVICE_POINT,
      mode: 'ACTIVATE_RETURN',
    });

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Xác nhận kích hoạt lượt khám lại' }),
      ).not.toBeDisabled();
    });

    expect(
      screen.getByText('Kích hoạt lượt khám lại đọc kết quả CLS (WAITING → READY)'),
    ).toBeInTheDocument();
    expect(screen.getByText('Giữ nguyên số phiếu #7')).toBeInTheDocument();
    expect(screen.getByText('Chờ kết quả CLS')).toBeInTheDocument();

    // Select reason without checking readiness/presence -> shows both superRefine errors
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Đã có đầy đủ kết quả cận lâm sàng bắt buộc, bệnh nhân có mặt sẵn sàng đọc kết quả',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận kích hoạt lượt khám lại' }));

    expect(
      screen.getByText(
        'Cần xác nhận đã có đầy đủ kết quả cận lâm sàng bắt buộc trước khi kích hoạt lượt khám lại sang Sẵn sàng (READY).',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Cần xác nhận bệnh nhân đã có mặt trở lại (PRESENT) trước khi kích hoạt lượt khám lại sang Sẵn sàng (READY).',
      ),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // Check both checkboxes and submit
    fireEvent.click(
      screen.getByLabelText('Đã có đầy đủ kết quả cận lâm sàng bắt buộc (final_result_required)'),
    );
    fireEvent.click(
      screen.getByLabelText(
        'Tôi xác nhận bệnh nhân đã có mặt trực tiếp tại khu vực phòng khám (presenceStatus = PRESENT)',
      ),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận kích hoạt lượt khám lại' }));

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith(mockActivatedResponse);
    });
    expect(postSpy).toHaveBeenCalledWith(
      '/reception/examination-queue/entries/q-009/return-to-doctor',
      {
        servicePointId: 'sp-room-103',
        confirmPresent: true,
        diagnosticResultsReady: true,
        reason: 'Đã có đầy đủ kết quả cận lâm sàng bắt buộc, bệnh nhân có mặt sẵn sàng đọc kết quả',
        notes: null,
        expectedStateVersion: 3,
      },
    );
  });

  it('surfaces HTTP 409 Conflict error in alert, refetches stateVersion on 409, and does not call onApplied', async () => {
    let statusGetCalls = 0;
    vi.spyOn(apiClient, 'get').mockImplementation(async () => {
      statusGetCalls += 1;
      return {
        data: {
          queueEntryId: 9,
          currentStatus: 'WAITING',
          stateVersion: statusGetCalls === 1 ? 2 : 3,
          allowedTargetStatuses: ['READY'],
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
            'Chưa thể kích hoạt lượt khám lại sang trạng thái Sẵn sàng (READY) vì kết quả cận lâm sàng bắt buộc chưa hoàn tất.',
        },
      },
    );

    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    const { onApplied } = renderModal({
      entry: WAITING_RETURN_ENTRY,
      servicePoint: SAMPLE_SERVICE_POINT,
      mode: 'ACTIVATE_RETURN',
    });

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Xác nhận kích hoạt lượt khám lại' }),
      ).not.toBeDisabled();
    });

    fireEvent.click(
      screen.getByLabelText('Đã có đầy đủ kết quả cận lâm sàng bắt buộc (final_result_required)'),
    );
    fireEvent.click(
      screen.getByLabelText(
        'Tôi xác nhận bệnh nhân đã có mặt trực tiếp tại khu vực phòng khám (presenceStatus = PRESENT)',
      ),
    );
    fireEvent.click(
      screen.getByRole('button', {
        name: 'Đã có đầy đủ kết quả cận lâm sàng bắt buộc, bệnh nhân có mặt sẵn sàng đọc kết quả',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận kích hoạt lượt khám lại' }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Chưa thể kích hoạt lượt khám lại sang trạng thái Sẵn sàng (READY) vì kết quả cận lâm sàng bắt buộc chưa hoàn tất.',
      );
    });
    expect(postSpy).toHaveBeenCalledTimes(1);
    expect(onApplied).not.toHaveBeenCalled();
    expect(statusGetCalls).toBe(2);
  });
});
