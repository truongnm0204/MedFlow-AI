export type ExaminationQueueStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED';

export type ExaminationQueuePriority = 'NORMAL' | 'ELDERLY' | 'PREGNANT';

export type ExaminationQueueType = 'EXAMINATION' | 'RETURN_CONSULTATION';
export type QueuePresenceStatus = 'PRESENT' | 'NOT_ARRIVED' | 'LEFT';

export type QueueArrivalClass = 'ON_TIME_OR_GRACE' | 'EARLY' | 'LATE' | 'WALK_IN';

export type ExaminationVisitType = 'SCHEDULED' | 'WALK_IN';

export type ExaminationQueueStatusFilter = 'ALL' | ExaminationQueueStatus;
export type ExaminationQueuePriorityFilter = 'ALL' | ExaminationQueuePriority;
export type ExaminationQueueTypeFilter = 'ALL' | ExaminationQueueType;
export type ExaminationVisitTypeFilter = 'ALL' | ExaminationVisitType;
export type QueueArrivalClassFilter = 'ALL' | QueueArrivalClass;
export type EarlyArrivalBackfillActionMode = 'SAFE_BACKFILL' | 'HOLD_FOR_SLOT';
export type QueueTurnActionMode = 'MISSED_TURN' | 'RESTORE_TURN';

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
  queueType: ExaminationQueueType;
  presenceStatus: QueuePresenceStatus;
  arrivalClass: QueueArrivalClass;
  scheduledAt: string | null;
  checkedInAt: string;
  calledAt: string | null;
  startedAt: string | null;
  blockedReason: string | null;
  isEligible?: boolean;
  canSafeBackfill?: boolean;
  isAppointmentProtected?: boolean;
  holdReason?: string | null;
  isBackfilled?: boolean;
  backfillReason?: string | null;
  backfillUpdatedAt?: string | null;
  queueGroup?:
    'IN_PROGRESS' | 'CALLED' | 'ELIGIBLE' | 'NOT_ELIGIBLE' | 'SKIPPED' | 'COMPLETED' | null;
  ineligibleReason?: 'NOT_ARRIVED' | 'LEFT' | 'BLOCKED' | 'AWAITING_RESULTS' | null;
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

export interface MarkMissedTurnRequestPayload {
  servicePointId: string;
  reason: string;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface RestoreQueueTurnRequestPayload {
  servicePointId: string;
  confirmPresent: boolean;
  reason: string;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface QueueTurnActionResponseDto {
  queueEntryId: string;
  ticketNo: string;
  queueNumber: number;
  visitCode: string;
  patientCode: string;
  patientName: string;
  servicePointId: string;
  action: QueueTurnActionMode;
  previousStatus: ExaminationQueueStatus;
  currentStatus: ExaminationQueueStatus;
  presenceStatus: QueuePresenceStatus;
  arrivalClass: QueueArrivalClass;
  previousServingOrder: number;
  servingOrder: number;
  isAppointmentProtected: boolean;
  blockedReason: string | null;
  reason: string;
  notes: string | null;
  previousStateVersion: number;
  stateVersion: number;
  updatedAtUtc: string;
  updatedEntry: ExaminationQueueEntryDto;
}

export interface LateArrivalMissedTurnTarget {
  entry: ExaminationQueueEntryDto;
  servicePoint: ServicePointQueueDto;
  mode: QueueTurnActionMode;
}

export const EXAMINATION_QUEUE_STATUS_VALUES = [
  'WAITING',
  'READY',
  'CALLED',
  'IN_PROGRESS',
  'COMPLETED',
  'SKIPPED',
] as const;

export const EXAMINATION_QUEUE_PRIORITY_VALUES = ['NORMAL', 'ELDERLY', 'PREGNANT'] as const;

export const QUEUE_PRESENCE_STATUS_VALUES = ['PRESENT', 'NOT_ARRIVED', 'LEFT'] as const;

export interface UpdateExaminationQueueStatusPayload {
  targetStatus: ExaminationQueueStatus;
  presenceStatus: QueuePresenceStatus;
  reason: string | null;
}

export interface UpdateExaminationQueueStatusResponseDto {
  entry: ExaminationQueueEntryDto;
  previousStatus: ExaminationQueueStatus;
  updatedAtUtc: string;
}

export type CallNextOutcome = 'CALLED' | 'NO_ELIGIBLE_PATIENT' | 'HOLD_FOR_SLOT';

export type CallNextSelectionReason =
  'APPOINTMENT_DUE' | 'RETURN_CONSULTATION' | 'PRIORITY' | 'SAFE_BACKFILL' | 'FIFO';

export interface CallNextPatientRequestPayload {
  notes?: string | null;
}

export interface CallNextPatientResponseDto {
  servicePointId: string;
  outcome: CallNextOutcome;
  message: string;
  selectionReason: CallNextSelectionReason | null;
  calledEntry: ExaminationQueueEntryDto | null;
  previousStateVersion: number | null;
  stateVersion: number | null;
  updatedAtUtc: string;
  entries: ExaminationQueueEntryDto[];
}
