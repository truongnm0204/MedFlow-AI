export type ExaminationQueueStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED';

export type ExaminationQueuePriority =
  'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT' | 'RETURN_RESULT';

export type QueuePresenceStatus = 'PRESENT' | 'NOT_ARRIVED' | 'LEFT';

export type QueueArrivalClass = 'ON_TIME_OR_GRACE' | 'EARLY' | 'LATE' | 'WALK_IN';

export type ExaminationVisitType = 'SCHEDULED' | 'WALK_IN';

export type ExaminationQueueStatusFilter = 'ALL' | ExaminationQueueStatus;
export type ExaminationQueuePriorityFilter = 'ALL' | ExaminationQueuePriority;
export type ExaminationVisitTypeFilter = 'ALL' | ExaminationVisitType;
export type QueueArrivalClassFilter = 'ALL' | QueueArrivalClass;
export type EarlyArrivalBackfillActionMode = 'SAFE_BACKFILL' | 'HOLD_FOR_SLOT';

export interface ExaminationQueueEntryDto {
  id: string;
  queueNumber: number;
  ticketNo: string;
  servingOrder: number;
  visitCode: string;
  patientName: string;
  patientCode: string;
  visitType: ExaminationVisitType;
  servicePointId: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  status: ExaminationQueueStatus;
  priority: ExaminationQueuePriority;
  presenceStatus: QueuePresenceStatus;
  arrivalClass: QueueArrivalClass;
  scheduledAt: string | null;
  checkedInAt: string;
  calledAt: string | null;
  startedAt: string | null;
  blockedReason: string | null;
  isBackfilled?: boolean;
  backfillReason?: string | null;
  backfillUpdatedAt?: string | null;
}

export interface ServicePointQueueDto {
  servicePointId: string;
  servicePointName: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  isActive: boolean;
  currentServingTicketNo: string | null;
  nextTicketNo: string | null;
  waitingCount: number;
  inProgressCount: number;
  completedCount: number;
  skippedCount: number;
  entries: ExaminationQueueEntryDto[];
}

export interface ExaminationQueueStatsDto {
  totalEntries: number;
  waitingCount: number;
  calledOrServingCount: number;
  completedCount: number;
  skippedCount: number;
  activeServicePoints: number;
}

export interface ExaminationQueueResponseDto {
  queueDate: string;
  facilityName: string;
  shiftLabel: string;
  generatedAtUtc: string;
  stats: ExaminationQueueStatsDto;
  servicePoints: ServicePointQueueDto[];
}

export interface ExaminationQueueQueryParams {
  queueDate?: string;
  servicePointId?: string;
  status?: ExaminationQueueStatusFilter;
  priority?: ExaminationQueuePriorityFilter;
  visitType?: ExaminationVisitTypeFilter;
  keyword?: string;
  arrivalClass?: QueueArrivalClassFilter;
}

export interface ProtectedAppointmentSummaryDto {
  id: string;
  ticketNo: string;
  visitCode: string;
  patientName: string;
  patientCode: string;
  scheduledAt: string | null;
  status: ExaminationQueueStatus;
}

export interface SafeBackfillEvaluation {
  isEligibleCandidate: boolean;
  canSafeBackfill: boolean;
  appointmentProtectionBlocked: boolean;
  hasIdleRoomGap: boolean;
  earlyLeadMinutes: number | null;
  protectedAppointments: ProtectedAppointmentSummaryDto[];
  statusReason: string;
  recommendedAction: EarlyArrivalBackfillActionMode;
}

export interface EarlyArrivalBackfillRequestPayload {
  servicePointId: string;
  actionMode: EarlyArrivalBackfillActionMode;
  arrivalClass: QueueArrivalClass;
  reason: string;
  notes?: string | null;
  acknowledgeAppointmentProtection: boolean;
}

export interface EarlyArrivalBackfillResponseDto {
  queueEntryId: string;
  ticketNo: string;
  queueNumber: number;
  visitCode: string;
  patientCode: string;
  patientName: string;
  servicePointId: string;
  roomCode: string;
  roomName: string;
  previousStatus: ExaminationQueueStatus;
  currentStatus: ExaminationQueueStatus;
  arrivalClass: QueueArrivalClass;
  servingOrder: number;
  isBackfilled: boolean;
  appointmentProtected: boolean;
  blockedReason: string | null;
  reason: string;
  notes: string | null;
  updatedAtUtc: string;
  updatedEntry: ExaminationQueueEntryDto;
}

export interface EarlyArrivalBackfillTarget {
  entry: ExaminationQueueEntryDto;
  servicePoint: ServicePointQueueDto;
}
