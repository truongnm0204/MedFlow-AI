import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  EarlyArrivalBackfillModal,
  type EarlyArrivalBackfillModalProps,
} from '../components/early-arrival-backfill-modal';
import { EXAMINATION_QUEUE_SERVICE_POINTS_MOCK } from '../data/examination-queue-mock';
import type {
  EarlyArrivalBackfillTarget,
  ExaminationQueueEntryDto,
  ServicePointQueueDto,
} from '../types/examination-queue.types';
import {
  computeEarlyLeadMinutes,
  evaluateSafeBackfillEligibility,
  parseTimeMinutes,
  recalculateServicePointServingOrder,
} from '../utils/early-arrival-backfill.utils';

function cloneServicePoint(sp: ServicePointQueueDto): ServicePointQueueDto {
  return {
    ...sp,
    entries: sp.entries.map((entry) => ({ ...entry })),
  };
}

function renderModal(props: {
  target: EarlyArrivalBackfillTarget;
  onClose?: () => void;
  onApplied?: EarlyArrivalBackfillModalProps['onApplied'];
  onSubmitOverride?: EarlyArrivalBackfillModalProps['onSubmitOverride'];
}) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={qc}>
      <EarlyArrivalBackfillModal
        open={true}
        target={props.target}
        onClose={props.onClose ?? vi.fn()}
        onApplied={props.onApplied ?? vi.fn()}
        onSubmitOverride={props.onSubmitOverride}
      />
    </QueryClientProvider>,
  );
}

describe('EarlyArrivalBackfillModal & Safe Backfill Utilities (UC28 - Issue #95)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('evaluates time parsing, early lead minutes, Safe Backfill eligibility, and Appointment Protection accurately', () => {
    expect(parseTimeMinutes('08:30')).toBe(510);
    expect(parseTimeMinutes('invalid')).toBeNull();
    expect(computeEarlyLeadMinutes('08:45', '08:31')).toBe(14);
    expect(computeEarlyLeadMinutes(null, '08:31')).toBeNull();

    // 1. P.102: q-004 (Phạm Thu Dung, A015, EARLY, READY) -> no waiting ON_TIME_OR_GRACE appointment -> eligible for Safe Backfill
    const room102 = cloneServicePoint(EXAMINATION_QUEUE_SERVICE_POINTS_MOCK[0]);
    const entryQ004 = room102.entries.find((e) => e.id === 'q-004')!;
    const evalRoom102 = evaluateSafeBackfillEligibility(entryQ004, room102);

    expect(evalRoom102.isEligibleCandidate).toBe(true);
    expect(evalRoom102.canSafeBackfill).toBe(true);
    expect(evalRoom102.appointmentProtectionBlocked).toBe(false);
    expect(evalRoom102.earlyLeadMinutes).toBe(14);
    expect(evalRoom102.recommendedAction).toBe('SAFE_BACKFILL');

    // 2. P.103: B006 (Vũ Thị Phương, ON_TIME_OR_GRACE, WAITING, PRESENT, scheduled 08:45) protects slot against normal early candidate
    const room103 = cloneServicePoint(EXAMINATION_QUEUE_SERVICE_POINTS_MOCK[1]);
    const earlyCandidateIn103: ExaminationQueueEntryDto = {
      id: 'q-early-103',
      queueNumber: 9,
      ticketNo: 'B009',
      servingOrder: 4,
      visitCode: 'VIS-2026-0199',
      patientName: 'Trịnh Văn Sớm',
      patientCode: 'BN-2024-0199',
      visitType: 'SCHEDULED',
      servicePointId: 'sp-room-103',
      roomCode: 'P.103',
      roomName: 'Phòng khám Tim mạch 1',
      departmentName: 'Khoa Tim mạch',
      doctorName: 'BS.CKI Lê Hoàng Nam',
      status: 'WAITING',
      priority: 'NORMAL',
      presenceStatus: 'PRESENT',
      arrivalClass: 'EARLY',
      scheduledAt: '09:30',
      checkedInAt: '08:35',
      calledAt: null,
      startedAt: null,
      blockedReason: null,
    };
    const room103WithEarly: ServicePointQueueDto = {
      ...room103,
      entries: [...room103.entries, earlyCandidateIn103],
    };

    const evalBlocked = evaluateSafeBackfillEligibility(earlyCandidateIn103, room103WithEarly);
    expect(evalBlocked.isEligibleCandidate).toBe(true);
    expect(evalBlocked.canSafeBackfill).toBe(false);
    expect(evalBlocked.appointmentProtectionBlocked).toBe(true);
    expect(evalBlocked.protectedAppointments).toHaveLength(1);
    expect(evalBlocked.protectedAppointments[0]?.ticketNo).toBe('B006');
    expect(evalBlocked.recommendedAction).toBe('HOLD_FOR_SLOT');
  });

  it('recalculates 1-based serving order while preserving immutable queueNumber and protecting ON_TIME_OR_GRACE ahead of normal backfill', () => {
    const entries: ExaminationQueueEntryDto[] = [
      {
        id: 'e-early-backfilled',
        queueNumber: 20,
        ticketNo: 'B020',
        servingOrder: 99,
        visitCode: 'VIS-2026-0200',
        patientName: 'Bệnh Nhân Đến Sớm',
        patientCode: 'BN-2024-0200',
        visitType: 'SCHEDULED',
        servicePointId: 'sp-room-103',
        roomCode: 'P.103',
        roomName: 'Phòng khám Tim mạch 1',
        departmentName: 'Khoa Tim mạch',
        doctorName: 'BS.CKI Lê Hoàng Nam',
        status: 'READY',
        priority: 'NORMAL',
        presenceStatus: 'PRESENT',
        arrivalClass: 'EARLY',
        scheduledAt: '09:30',
        checkedInAt: '08:10',
        calledAt: null,
        startedAt: null,
        blockedReason: null,
        isBackfilled: true,
      },
      {
        id: 'e-ontime-protected',
        queueNumber: 18,
        ticketNo: 'B018',
        servingOrder: 99,
        visitCode: 'VIS-2026-0180',
        patientName: 'Bệnh Nhân Đúng Giờ',
        patientCode: 'BN-2024-0180',
        visitType: 'SCHEDULED',
        servicePointId: 'sp-room-103',
        roomCode: 'P.103',
        roomName: 'Phòng khám Tim mạch 1',
        departmentName: 'Khoa Tim mạch',
        doctorName: 'BS.CKI Lê Hoàng Nam',
        status: 'WAITING',
        priority: 'NORMAL',
        presenceStatus: 'PRESENT',
        arrivalClass: 'ON_TIME_OR_GRACE',
        scheduledAt: '08:30',
        checkedInAt: '08:28',
        calledAt: null,
        startedAt: null,
        blockedReason: null,
      },
    ];

    const reordered = recalculateServicePointServingOrder(entries);

    expect(reordered[0]?.id).toBe('e-ontime-protected');
    expect(reordered[0]?.servingOrder).toBe(1);
    expect(reordered[0]?.queueNumber).toBe(18);
    expect(reordered[0]?.ticketNo).toBe('B018');

    expect(reordered[1]?.id).toBe('e-early-backfilled');
    expect(reordered[1]?.servingOrder).toBe(2);
    expect(reordered[1]?.queueNumber).toBe(20);
    expect(reordered[1]?.ticketNo).toBe('B020');
  });

  it('validates reason length, angle brackets, and appointment protection acknowledgment in EarlyArrivalBackfillModal', async () => {
    const room102 = cloneServicePoint(EXAMINATION_QUEUE_SERVICE_POINTS_MOCK[0]);
    const entryQ004 = {
      ...room102.entries.find((e) => e.id === 'q-004')!,
      backfillReason: null,
    };

    renderModal({
      target: { entry: entryQ004, servicePoint: room102 },
    });

    expect(screen.getByText('Xử lý bệnh nhân đến sớm & Safe Backfill (UC28)')).toBeInTheDocument();
    expect(screen.getByText('Giữ nguyên số phiếu #15')).toBeInTheDocument();

    const reasonInput = screen.getByLabelText('Lý do điều phối đến sớm / Safe Backfill');

    // 1. Short reason (< 5 chars)
    fireEvent.change(reasonInput, { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận điều phối' }));
    expect(
      await screen.findByText('Vui lòng nhập lý do xử lý tối thiểu 5 ký tự.'),
    ).toBeInTheDocument();

    // 2. Invalid characters (<script>)
    fireEvent.change(reasonInput, { target: { value: '<script>alert(1)</script>' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận điều phối' }));
    expect(await screen.findByText('Lý do xử lý chứa ký tự không hợp lệ.')).toBeInTheDocument();

    // 3. Uncheck appointment protection acknowledgment while SAFE_BACKFILL is selected
    fireEvent.change(reasonInput, {
      target: { value: 'Bệnh nhân đến sớm, phòng khám đang trống lượt hẹn đúng giờ' },
    });
    const checkbox = screen.getByRole('checkbox');
    fireEvent.click(checkbox);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận điều phối' }));

    expect(
      await screen.findByText(
        'Vui lòng xác nhận cam kết bảo vệ lịch hẹn đúng giờ và giữ nguyên số thứ tự trước khi Safe Backfill.',
      ),
    ).toBeInTheDocument();
  });

  it('displays Appointment Protection warning banner and disables SAFE_BACKFILL when an on-time appointment is waiting', () => {
    const room103 = cloneServicePoint(EXAMINATION_QUEUE_SERVICE_POINTS_MOCK[1]);
    const earlyCandidateIn103: ExaminationQueueEntryDto = {
      id: 'q-early-103',
      queueNumber: 9,
      ticketNo: 'B009',
      servingOrder: 4,
      visitCode: 'VIS-2026-0199',
      patientName: 'Trịnh Văn Sớm',
      patientCode: 'BN-2024-0199',
      visitType: 'SCHEDULED',
      servicePointId: 'sp-room-103',
      roomCode: 'P.103',
      roomName: 'Phòng khám Tim mạch 1',
      departmentName: 'Khoa Tim mạch',
      doctorName: 'BS.CKI Lê Hoàng Nam',
      status: 'WAITING',
      priority: 'NORMAL',
      presenceStatus: 'PRESENT',
      arrivalClass: 'EARLY',
      scheduledAt: '09:30',
      checkedInAt: '08:35',
      calledAt: null,
      startedAt: null,
      blockedReason: null,
    };
    const room103WithEarly: ServicePointQueueDto = {
      ...room103,
      entries: [...room103.entries, earlyCandidateIn103],
    };

    renderModal({
      target: { entry: earlyCandidateIn103, servicePoint: room103WithEarly },
    });

    expect(
      screen.getByText(/Đang bảo vệ khung giờ hẹn của bệnh nhân đúng giờ/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/B006 • Vũ Thị Phương/i)).toBeInTheDocument();

    const radios = screen.getAllByRole('radio') as HTMLInputElement[];
    const safeBackfillRadio = radios.find((r) => r.value === 'SAFE_BACKFILL')!;
    const holdForSlotRadio = radios.find((r) => r.value === 'HOLD_FOR_SLOT')!;

    expect(safeBackfillRadio).toBeDisabled();
    expect(holdForSlotRadio).toBeChecked();
  });

  it('submits POST /reception/examination-queue/entries/:id/early-arrival-backfill and surfaces HTTP 409 Conflict error in alert', async () => {
    window.localStorage.setItem('medflow.access_token', 'jwt-reception-token');

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
            'Không thể Safe Backfill: Khung giờ hẹn 08:45 đang được bảo vệ cho bệnh nhân đúng giờ.',
        },
      },
    );

    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    const room102 = cloneServicePoint(EXAMINATION_QUEUE_SERVICE_POINTS_MOCK[0]);
    const entryQ004 = room102.entries.find((e) => e.id === 'q-004')!;
    const onApplied = vi.fn();

    renderModal({
      target: { entry: entryQ004, servicePoint: room102 },
      onApplied,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận điều phối' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/reception/examination-queue/entries/q-004/early-arrival-backfill',
        expect.objectContaining({
          servicePointId: 'sp-room-102',
          actionMode: 'SAFE_BACKFILL',
          arrivalClass: 'EARLY',
          acknowledgeAppointmentProtection: true,
        }),
      );
    });

    expect(
      await screen.findByText(
        'Không thể Safe Backfill: Khung giờ hẹn 08:45 đang được bảo vệ cho bệnh nhân đúng giờ.',
      ),
    ).toBeInTheDocument();
    expect(onApplied).not.toHaveBeenCalled();
  });
});
