import type {
  EarlyArrivalBackfillRequestPayload,
  EarlyArrivalBackfillResponseDto,
  ExaminationQueueEntryDto,
  ExaminationQueuePriority,
  ExaminationQueueQueryParams,
  ExaminationQueueResponseDto,
  ExaminationQueueStatsDto,
  ExaminationQueueStatus,
  QueueArrivalClass,
  ServicePointQueueDto,
  UpdateExaminationQueueStatusPayload,
  UpdateExaminationQueueStatusResponseDto,
} from '../types/examination-queue.types';
import {
  evaluateSafeBackfillEligibility,
  recalculateServicePointServingOrder,
} from '../utils/early-arrival-backfill.utils';

export const DEFAULT_QUEUE_DATE = '2026-11-04';

export const EXAMINATION_QUEUE_SERVICE_POINTS_MOCK: ServicePointQueueDto[] = [
  {
    servicePointId: 'sp-room-102',
    servicePointName: 'Điểm khám Nội tổng quát 1',
    roomCode: 'P.102',
    roomName: 'Phòng khám Nội tổng quát 1',
    departmentName: 'Khoa Nội tổng quát',
    doctorName: 'BS.CKII Trần Khánh',
    isActive: true,
    currentServingTicketNo: 'A012',
    nextTicketNo: 'A013',
    waitingCount: 2,
    inProgressCount: 1,
    completedCount: 1,
    skippedCount: 0,
    entries: [
      {
        id: 'q-001',
        queueNumber: 12,
        ticketNo: 'A012',
        servingOrder: 1,
        visitCode: 'VIS-2026-0101',
        patientName: 'Nguyễn Văn An',
        patientCode: 'BN-2024-0018',
        visitType: 'SCHEDULED',
        servicePointId: 'sp-room-102',
        roomCode: 'P.102',
        roomName: 'Phòng khám Nội tổng quát 1',
        departmentName: 'Khoa Nội tổng quát',
        doctorName: 'BS.CKII Trần Khánh',
        status: 'IN_PROGRESS',
        priority: 'NORMAL',
        presenceStatus: 'PRESENT',
        arrivalClass: 'ON_TIME_OR_GRACE',
        scheduledAt: '08:00',
        checkedInAt: '08:02',
        calledAt: '08:05',
        startedAt: '08:07',
        blockedReason: null,
      },
      {
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
        presenceStatus: 'PRESENT',
        arrivalClass: 'ON_TIME_OR_GRACE',
        scheduledAt: '08:15',
        checkedInAt: '08:12',
        calledAt: '08:18',
        startedAt: null,
        blockedReason: null,
      },
      {
        id: 'q-004',
        queueNumber: 15,
        ticketNo: 'A015',
        servingOrder: 3,
        visitCode: 'VIS-2026-0104',
        patientName: 'Phạm Thu Dung',
        patientCode: 'BN-2024-0089',
        visitType: 'SCHEDULED',
        servicePointId: 'sp-room-102',
        roomCode: 'P.102',
        roomName: 'Phòng khám Nội tổng quát 1',
        departmentName: 'Khoa Nội tổng quát',
        doctorName: 'BS.CKII Trần Khánh',
        status: 'READY',
        priority: 'PREGNANT',
        presenceStatus: 'PRESENT',
        arrivalClass: 'EARLY',
        scheduledAt: '08:45',
        checkedInAt: '08:31',
        calledAt: null,
        startedAt: null,
        blockedReason: null,
        isBackfilled: true,
        backfillReason: 'Đến sớm 14 phút, phòng P.102 trống lượt hẹn đúng giờ',
      },
      {
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
        presenceStatus: 'PRESENT',
        arrivalClass: 'ON_TIME_OR_GRACE',
        scheduledAt: '07:30',
        checkedInAt: '07:28',
        calledAt: '07:35',
        startedAt: '07:37',
        blockedReason: null,
      },
    ],
  },
  {
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
    entries: [
      {
        id: 'q-003',
        queueNumber: 5,
        ticketNo: 'B005',
        servingOrder: 1,
        visitCode: 'VIS-2026-0103',
        patientName: 'Lê Minh Cường',
        patientCode: 'BN-2024-0071',
        visitType: 'WALK_IN',
        servicePointId: 'sp-room-103',
        roomCode: 'P.103',
        roomName: 'Phòng khám Tim mạch 1',
        departmentName: 'Khoa Tim mạch',
        doctorName: 'BS.CKI Lê Hoàng Nam',
        status: 'IN_PROGRESS',
        priority: 'RETURN_RESULT',
        presenceStatus: 'PRESENT',
        arrivalClass: 'WALK_IN',
        scheduledAt: null,
        checkedInAt: '08:22',
        calledAt: '08:25',
        startedAt: '08:27',
        blockedReason: null,
      },
      {
        id: 'q-006',
        queueNumber: 6,
        ticketNo: 'B006',
        servingOrder: 2,
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
        presenceStatus: 'PRESENT',
        arrivalClass: 'ON_TIME_OR_GRACE',
        scheduledAt: '08:45',
        checkedInAt: '08:47',
        calledAt: null,
        startedAt: null,
        blockedReason: null,
      },
      {
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
        presenceStatus: 'NOT_ARRIVED',
        arrivalClass: 'LATE',
        scheduledAt: '08:00',
        checkedInAt: '08:19',
        calledAt: '08:20',
        startedAt: null,
        blockedReason: 'Gọi 3 lần chưa có mặt tại cửa phòng khám',
      },
    ],
  },
  {
    servicePointId: 'sp-room-104',
    servicePointName: 'Điểm khám Tai Mũi Họng',
    roomCode: 'P.104',
    roomName: 'Phòng khám Tai Mũi Họng',
    departmentName: 'Khoa Tai Mũi Họng',
    doctorName: 'BS.CKI Đinh Long',
    isActive: true,
    currentServingTicketNo: null,
    nextTicketNo: 'C001',
    waitingCount: 1,
    inProgressCount: 0,
    completedCount: 0,
    skippedCount: 0,
    entries: [
      {
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
        priority: 'URGENT',
        presenceStatus: 'PRESENT',
        arrivalClass: 'WALK_IN',
        scheduledAt: null,
        checkedInAt: '08:40',
        calledAt: null,
        startedAt: null,
        blockedReason: null,
      },
    ],
  },
];

export function buildMockExaminationQueueResponse(
  params?: ExaminationQueueQueryParams,
): ExaminationQueueResponseDto {
  const allEntries = EXAMINATION_QUEUE_SERVICE_POINTS_MOCK.flatMap((sp) => sp.entries);
  const waitingCount = allEntries.filter(
    (entry) => entry.status === 'WAITING' || entry.status === 'READY',
  ).length;
  const calledOrServingCount = allEntries.filter(
    (entry) => entry.status === 'CALLED' || entry.status === 'IN_PROGRESS',
  ).length;
  const completedCount = allEntries.filter((entry) => entry.status === 'COMPLETED').length;
  const skippedCount = allEntries.filter((entry) => entry.status === 'SKIPPED').length;

  return {
    queueDate: params?.queueDate || DEFAULT_QUEUE_DATE,
    facilityName: 'Bệnh viện Đa khoa Trung ương MedFlow',
    shiftLabel: 'Ca sáng (07:30 - 11:30)',
    generatedAtUtc: '2026-11-04T01:50:00Z',
    stats: {
      totalEntries: allEntries.length,
      waitingCount,
      calledOrServingCount,
      completedCount,
      skippedCount,
      activeServicePoints: EXAMINATION_QUEUE_SERVICE_POINTS_MOCK.filter((sp) => sp.isActive).length,
    },
    servicePoints: EXAMINATION_QUEUE_SERVICE_POINTS_MOCK.map((sp) => ({
      ...sp,
      entries: sp.entries.map((entry) => ({ ...entry })),
    })),
  };
}

export function buildMockEarlyArrivalBackfillResponse(
  queueEntryId: string,
  payload: EarlyArrivalBackfillRequestPayload,
): EarlyArrivalBackfillResponseDto {
  const normalizedId = queueEntryId.trim();
  let targetServicePoint: ServicePointQueueDto | undefined;
  let targetEntry: ExaminationQueueEntryDto | undefined;

  for (const sp of EXAMINATION_QUEUE_SERVICE_POINTS_MOCK) {
    const found = sp.entries.find((item) => item.id === normalizedId);
    if (found) {
      targetServicePoint = sp;
      targetEntry = found;
      break;
    }
  }

  if (!targetServicePoint || !targetEntry) {
    throw new Error('Không tìm thấy lượt khám trong hàng đợi.');
  }

  const evaluation = evaluateSafeBackfillEligibility(targetEntry, targetServicePoint);
  if (payload.actionMode === 'SAFE_BACKFILL' && evaluation.appointmentProtectionBlocked) {
    throw new Error(evaluation.statusReason);
  }

  const nextStatus = payload.actionMode === 'SAFE_BACKFILL' ? 'READY' : 'WAITING';
  const nextIsBackfilled = payload.actionMode === 'SAFE_BACKFILL';
  const nextBlockedReason =
    payload.actionMode === 'HOLD_FOR_SLOT'
      ? `Giữ chờ tới khung giờ hẹn ${targetEntry.scheduledAt ?? ''} (Bảo vệ lịch hẹn đúng giờ)`.trim()
      : null;

  const draftEntry: ExaminationQueueEntryDto = {
    ...targetEntry,
    status: nextStatus,
    arrivalClass: payload.arrivalClass,
    isBackfilled: nextIsBackfilled,
    backfillReason: payload.reason,
    backfillUpdatedAt: '08:36',
    blockedReason: nextBlockedReason,
  };

  const recalculated = recalculateServicePointServingOrder(
    targetServicePoint.entries.map((item) => (item.id === targetEntry.id ? draftEntry : item)),
  );
  const recalculatedEntry = recalculated.find((item) => item.id === targetEntry.id) ?? draftEntry;

  return {
    queueEntryId: targetEntry.id,
    ticketNo: targetEntry.ticketNo,
    queueNumber: targetEntry.queueNumber,
    visitCode: targetEntry.visitCode,
    patientCode: targetEntry.patientCode,
    patientName: targetEntry.patientName,
    servicePointId: targetServicePoint.servicePointId,
    roomCode: targetServicePoint.roomCode,
    roomName: targetServicePoint.roomName,
    previousStatus: targetEntry.status,
    currentStatus: recalculatedEntry.status,
    arrivalClass: recalculatedEntry.arrivalClass,
    servingOrder: recalculatedEntry.servingOrder,
    isBackfilled: nextIsBackfilled,
    appointmentProtected: true,
    blockedReason: recalculatedEntry.blockedReason,
    reason: payload.reason,
    notes: payload.notes ?? null,
    updatedAtUtc: '2026-11-04T01:55:00Z',
    updatedEntry: recalculatedEntry,
  };
}

export const ALLOWED_QUEUE_STATUS_TRANSITIONS: Readonly<
  Record<ExaminationQueueStatus, readonly ExaminationQueueStatus[]>
> = {
  WAITING: ['READY', 'CALLED', 'SKIPPED'],
  READY: ['CALLED', 'WAITING', 'SKIPPED'],
  CALLED: ['IN_PROGRESS', 'READY', 'WAITING', 'SKIPPED'],
  IN_PROGRESS: ['COMPLETED'],
  SKIPPED: ['WAITING', 'READY'],
  COMPLETED: [],
};

export function getAllowedNextQueueStatuses(
  currentStatus: ExaminationQueueStatus,
): readonly ExaminationQueueStatus[] {
  return ALLOWED_QUEUE_STATUS_TRANSITIONS[currentStatus] ?? [];
}

export function isAllowedQueueStatusTransition(
  currentStatus: ExaminationQueueStatus,
  targetStatus: ExaminationQueueStatus,
): boolean {
  return getAllowedNextQueueStatuses(currentStatus).includes(targetStatus);
}

const STATUS_SORT_RANK: Readonly<Record<ExaminationQueueStatus, number>> = {
  IN_PROGRESS: 0,
  CALLED: 1,
  READY: 2,
  WAITING: 3,
  SKIPPED: 4,
  COMPLETED: 5,
};

const PRIORITY_SORT_RANK: Readonly<Record<ExaminationQueuePriority, number>> = {
  URGENT: 0,
  RETURN_RESULT: 1,
  ELDERLY: 2,
  PREGNANT: 3,
  NORMAL: 4,
};

const ARRIVAL_CLASS_SORT_RANK: Readonly<Record<QueueArrivalClass, number>> = {
  ON_TIME_OR_GRACE: 0,
  EARLY: 1,
  WALK_IN: 2,
  LATE: 3,
};

function sortAndReindexServicePointEntries(
  entries: ExaminationQueueEntryDto[],
): ExaminationQueueEntryDto[] {
  return [...entries]
    .sort((a, b) => {
      const statusDiff = STATUS_SORT_RANK[a.status] - STATUS_SORT_RANK[b.status];
      if (statusDiff !== 0) return statusDiff;

      const priorityDiff = PRIORITY_SORT_RANK[a.priority] - PRIORITY_SORT_RANK[b.priority];
      if (priorityDiff !== 0) return priorityDiff;

      const arrivalDiff =
        ARRIVAL_CLASS_SORT_RANK[a.arrivalClass] - ARRIVAL_CLASS_SORT_RANK[b.arrivalClass];
      if (arrivalDiff !== 0) return arrivalDiff;

      return a.checkedInAt.localeCompare(b.checkedInAt);
    })
    .map((entry, index) => ({
      ...entry,
      servingOrder: index + 1,
    }));
}

function recalculateServicePointQueue(
  servicePoint: ServicePointQueueDto,
  entries: ExaminationQueueEntryDto[],
): ServicePointQueueDto {
  const sortedEntries = sortAndReindexServicePointEntries(entries);
  const currentServing = sortedEntries.find(
    (e) => e.status === 'IN_PROGRESS' || e.status === 'CALLED',
  );
  const nextTicket = sortedEntries.find((e) => e.status === 'READY' || e.status === 'WAITING');

  return {
    ...servicePoint,
    currentServingTicketNo: currentServing?.ticketNo ?? null,
    nextTicketNo: nextTicket?.ticketNo ?? null,
    waitingCount: sortedEntries.filter((e) => e.status === 'WAITING' || e.status === 'READY')
      .length,
    inProgressCount: sortedEntries.filter((e) => e.status === 'IN_PROGRESS').length,
    completedCount: sortedEntries.filter((e) => e.status === 'COMPLETED').length,
    skippedCount: sortedEntries.filter((e) => e.status === 'SKIPPED').length,
    entries: sortedEntries,
  };
}

function recalculateQueueStats(servicePoints: ServicePointQueueDto[]): ExaminationQueueStatsDto {
  const allEntries = servicePoints.flatMap((sp) => sp.entries);
  return {
    totalEntries: allEntries.length,
    waitingCount: allEntries.filter((e) => e.status === 'WAITING' || e.status === 'READY').length,
    calledOrServingCount: allEntries.filter(
      (e) => e.status === 'CALLED' || e.status === 'IN_PROGRESS',
    ).length,
    completedCount: allEntries.filter((e) => e.status === 'COMPLETED').length,
    skippedCount: allEntries.filter((e) => e.status === 'SKIPPED').length,
    activeServicePoints: servicePoints.filter((sp) => sp.isActive).length,
  };
}

export function applyQueueStatusUpdateToResponse(
  response: ExaminationQueueResponseDto,
  updatedEntry: ExaminationQueueEntryDto,
): ExaminationQueueResponseDto {
  const servicePoints = response.servicePoints.map((sp) => {
    const hasEntry = sp.entries.some((entry) => entry.id === updatedEntry.id);
    if (!hasEntry) {
      return {
        ...sp,
        entries: sp.entries.map((entry) => ({ ...entry })),
      };
    }

    const updatedEntries = sp.entries.map((entry) =>
      entry.id === updatedEntry.id ? { ...updatedEntry } : { ...entry },
    );
    return recalculateServicePointQueue(sp, updatedEntries);
  });

  return {
    ...response,
    stats: recalculateQueueStats(servicePoints),
    servicePoints,
  };
}

export function buildMockUpdateExaminationQueueStatusResponse(
  queueEntryId: string,
  payload: UpdateExaminationQueueStatusPayload,
  currentData?: ExaminationQueueResponseDto,
): UpdateExaminationQueueStatusResponseDto {
  const sourceServicePoints = currentData?.servicePoints ?? EXAMINATION_QUEUE_SERVICE_POINTS_MOCK;
  const existingEntry = sourceServicePoints
    .flatMap((sp) => sp.entries)
    .find((entry) => entry.id === queueEntryId);

  if (!existingEntry) {
    throw new Error('Không tìm thấy lượt hàng đợi cần cập nhật.');
  }

  if (!isAllowedQueueStatusTransition(existingEntry.status, payload.targetStatus)) {
    throw new Error('Chuyển tiếp trạng thái hàng đợi không hợp lệ.');
  }

  const trimmedReason = payload.reason?.trim() || null;

  let calledAt = existingEntry.calledAt;
  if (payload.targetStatus === 'CALLED' || payload.targetStatus === 'IN_PROGRESS') {
    calledAt = existingEntry.calledAt ?? '08:50';
  } else if (payload.targetStatus === 'WAITING' || payload.targetStatus === 'READY') {
    calledAt = null;
  }

  let startedAt = existingEntry.startedAt;
  if (payload.targetStatus === 'IN_PROGRESS') {
    startedAt = existingEntry.startedAt ?? '08:52';
  } else if (
    payload.targetStatus === 'WAITING' ||
    payload.targetStatus === 'READY' ||
    payload.targetStatus === 'CALLED'
  ) {
    startedAt = null;
  }

  let blockedReason = existingEntry.blockedReason;
  if (payload.targetStatus === 'SKIPPED' || trimmedReason) {
    blockedReason = trimmedReason;
  } else if (existingEntry.status === 'SKIPPED' && !trimmedReason) {
    blockedReason = null;
  }

  const rawUpdatedEntry: ExaminationQueueEntryDto = {
    ...existingEntry,
    status: payload.targetStatus,
    presenceStatus: payload.presenceStatus,
    calledAt,
    startedAt,
    blockedReason,
  };

  const baseResponse = currentData ?? buildMockExaminationQueueResponse();
  const updatedResponse = applyQueueStatusUpdateToResponse(baseResponse, rawUpdatedEntry);
  const reindexedEntry =
    updatedResponse.servicePoints
      .flatMap((sp) => sp.entries)
      .find((entry) => entry.id === queueEntryId) ?? rawUpdatedEntry;

  return {
    entry: reindexedEntry,
    previousStatus: existingEntry.status,
    updatedAtUtc: '2026-11-04T01:55:00Z',
  };
}
