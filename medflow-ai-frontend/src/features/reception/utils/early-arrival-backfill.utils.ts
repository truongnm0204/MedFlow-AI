import type {
  ExaminationQueueEntryDto,
  ExaminationQueuePriority,
  ProtectedAppointmentSummaryDto,
  SafeBackfillEvaluation,
  ServicePointQueueDto,
} from '../types/examination-queue.types';

export const EARLY_ARRIVAL_THRESHOLD_MINUTES = 15;

export function parseTimeMinutes(hhmm: string | null | undefined): number | null {
  if (!hhmm || typeof hhmm !== 'string') {
    return null;
  }

  const trimmed = hhmm.trim();
  const match = /^(\d{1,2}):(\d{2})$/.exec(trimmed);
  if (!match) {
    return null;
  }

  const hours = Number.parseInt(match[1], 10);
  const minutes = Number.parseInt(match[2], 10);
  if (
    Number.isNaN(hours) ||
    Number.isNaN(minutes) ||
    hours < 0 ||
    hours > 23 ||
    minutes < 0 ||
    minutes > 59
  ) {
    return null;
  }

  return hours * 60 + minutes;
}

export function computeEarlyLeadMinutes(
  scheduledAt: string | null,
  checkedInAt: string,
): number | null {
  const scheduledMinutes = parseTimeMinutes(scheduledAt);
  const checkedInMinutes = parseTimeMinutes(checkedInAt);
  if (scheduledMinutes === null || checkedInMinutes === null) {
    return null;
  }

  return scheduledMinutes - checkedInMinutes;
}

export function evaluateSafeBackfillEligibility(
  entry: ExaminationQueueEntryDto,
  servicePoint: ServicePointQueueDto,
): SafeBackfillEvaluation {
  const entryScheduledMinutes = parseTimeMinutes(entry.scheduledAt);

  const protectedAppointments: ProtectedAppointmentSummaryDto[] = servicePoint.entries
    .filter((other) => {
      if (other.id === entry.id) {
        return false;
      }
      if (other.visitType !== 'SCHEDULED') {
        return false;
      }
      if (other.arrivalClass !== 'ON_TIME_OR_GRACE') {
        return false;
      }
      if (other.presenceStatus !== 'PRESENT') {
        return false;
      }
      if (other.status !== 'WAITING' && other.status !== 'READY') {
        return false;
      }

      if (entry.scheduledAt === null || entryScheduledMinutes === null) {
        return true;
      }

      const otherScheduledMinutes = parseTimeMinutes(other.scheduledAt);
      if (otherScheduledMinutes === null) {
        return true;
      }

      return otherScheduledMinutes <= entryScheduledMinutes;
    })
    .map((other) => ({
      id: other.id,
      ticketNo: other.ticketNo,
      visitCode: other.visitCode,
      patientName: other.patientName,
      patientCode: other.patientCode,
      scheduledAt: other.scheduledAt,
      status: other.status,
    }));

  const earlyLeadMinutes = computeEarlyLeadMinutes(entry.scheduledAt, entry.checkedInAt);
  const hasIdleRoomGap = protectedAppointments.length === 0;

  if (entry.status !== 'WAITING' && entry.status !== 'READY') {
    return {
      isEligibleCandidate: false,
      canSafeBackfill: false,
      appointmentProtectionBlocked: false,
      hasIdleRoomGap,
      earlyLeadMinutes,
      protectedAppointments,
      statusReason:
        'Chỉ hỗ trợ điều phối đến sớm / Safe Backfill cho lượt khám đang ở trạng thái Đang chờ hoặc Sẵn sàng.',
      recommendedAction: 'HOLD_FOR_SLOT',
    };
  }

  if (entry.presenceStatus !== 'PRESENT') {
    return {
      isEligibleCandidate: true,
      canSafeBackfill: false,
      appointmentProtectionBlocked: false,
      hasIdleRoomGap,
      earlyLeadMinutes,
      protectedAppointments,
      statusReason:
        'Bệnh nhân chưa có mặt tại điểm dịch vụ (trạng thái hiện diện phải là Có mặt trước khi Safe Backfill).',
      recommendedAction: 'HOLD_FOR_SLOT',
    };
  }

  if (protectedAppointments.length > 0 && entry.priority === 'NORMAL') {
    return {
      isEligibleCandidate: true,
      canSafeBackfill: false,
      appointmentProtectionBlocked: true,
      hasIdleRoomGap: false,
      earlyLeadMinutes,
      protectedAppointments,
      statusReason: `Đang bảo vệ khung giờ hẹn của bệnh nhân đúng giờ (${protectedAppointments.map((p) => `${p.ticketNo} - ${p.patientName}`).join(', ')}). Không thể chèn khám sớm trước lượt hẹn được bảo vệ.`,
      recommendedAction: 'HOLD_FOR_SLOT',
    };
  }

  return {
    isEligibleCandidate: true,
    canSafeBackfill: true,
    appointmentProtectionBlocked: false,
    hasIdleRoomGap,
    earlyLeadMinutes,
    protectedAppointments,
    statusReason:
      'Phòng khám đang có khoảng trống an toàn và không xâm phạm lịch hẹn đúng giờ. Đủ điều kiện áp dụng Safe Backfill.',
    recommendedAction: 'SAFE_BACKFILL',
  };
}

const STATUS_BUCKET_RANK: Record<ExaminationQueueEntryDto['status'], number> = {
  IN_PROGRESS: 0,
  CALLED: 1,
  READY: 2,
  WAITING: 2,
  COMPLETED: 3,
  SKIPPED: 4,
};

const PRIORITY_RANK: Record<ExaminationQueuePriority, number> = {
  ELDERLY: 0,
  PREGNANT: 0,
  NORMAL: 1,
};

function getBackfillFairnessTier(entry: ExaminationQueueEntryDto): number {
  if (entry.arrivalClass === 'ON_TIME_OR_GRACE' && entry.presenceStatus === 'PRESENT') {
    return 0;
  }
  if (entry.isBackfilled === true || entry.status === 'READY') {
    return 1;
  }
  return 2;
}

export function recalculateServicePointServingOrder(
  entries: ExaminationQueueEntryDto[],
): ExaminationQueueEntryDto[] {
  const sorted = entries.map((entry) => ({ ...entry }));

  sorted.sort((a, b) => {
    const bucketA = STATUS_BUCKET_RANK[a.status];
    const bucketB = STATUS_BUCKET_RANK[b.status];
    if (bucketA !== bucketB) {
      return bucketA - bucketB;
    }

    if (bucketA === 2) {
      const priorityA = PRIORITY_RANK[a.priority];
      const priorityB = PRIORITY_RANK[b.priority];
      if (priorityA !== priorityB) {
        return priorityA - priorityB;
      }

      const tierA = getBackfillFairnessTier(a);
      const tierB = getBackfillFairnessTier(b);
      if (tierA !== tierB) {
        return tierA - tierB;
      }
    }

    const checkInA = parseTimeMinutes(a.checkedInAt) ?? 9999;
    const checkInB = parseTimeMinutes(b.checkedInAt) ?? 9999;
    if (checkInA !== checkInB) {
      return checkInA - checkInB;
    }

    return a.queueNumber - b.queueNumber;
  });

  return sorted.map((entry, index) => ({
    ...entry,
    servingOrder: index + 1,
  }));
}
