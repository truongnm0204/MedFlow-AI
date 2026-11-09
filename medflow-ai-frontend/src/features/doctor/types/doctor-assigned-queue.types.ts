import type { AssignedQueueType, VisitPriority } from './doctor.types';

export type DoctorQueueEntryStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED';

export type DoctorQueueGroup =
  'IN_PROGRESS' | 'CALLED' | 'ELIGIBLE' | 'NOT_ELIGIBLE' | 'SKIPPED' | 'COMPLETED';

export type DoctorQueueIneligibleReason = 'NOT_ARRIVED' | 'LEFT' | 'BLOCKED' | 'AWAITING_RESULTS';

export type DoctorQueuePresenceStatus = 'PRESENT' | 'NOT_ARRIVED' | 'LEFT';

export type DoctorQueueArrivalClass = 'ON_TIME_OR_GRACE' | 'EARLY' | 'LATE' | 'WALK_IN';

export type DoctorQueueVisitType = 'SCHEDULED' | 'WALK_IN';

export type DoctorQueueGroupFilter = 'ALL' | DoctorQueueGroup;

export interface DoctorAssignedQueueEntryDto {
  id: string;
  queueNumber: number;
  ticketNo: string;
  servingOrder: number;
  visitCode: string;
  patientName: string;
  patientCode: string;
  visitType: DoctorQueueVisitType;
  servicePointId: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  status: DoctorQueueEntryStatus;
  priority: VisitPriority;
  queueType: AssignedQueueType;
  presenceStatus: DoctorQueuePresenceStatus;
  arrivalClass: DoctorQueueArrivalClass;
  scheduledAt: string | null;
  checkedInAt: string;
  calledAt: string | null;
  startedAt: string | null;
  blockedReason: string | null;
  isEligible: boolean;
  isAppointmentProtected: boolean;
  isBackfilled: boolean;
  backfillReason: string | null;
  queueGroup: DoctorQueueGroup;
  ineligibleReason: DoctorQueueIneligibleReason | null;
  canSafeBackfill?: boolean;
  holdReason?: string | null;
  backfillUpdatedAt?: string | null;
}

export interface DoctorAssignedQueueStatsDto {
  totalEntries: number;
  inProgressCount: number;
  calledCount: number;
  eligibleCount: number;
  notEligibleCount: number;
  skippedCount: number;
  completedCount: number;
}

export interface DoctorAssignedServicePointDto {
  servicePointId: string;
  servicePointName: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  isActive: boolean;
  currentServingTicketNo: string | null;
  nextEligibleTicketNo: string | null;
  stats: DoctorAssignedQueueStatsDto;
  entries: DoctorAssignedQueueEntryDto[];
}

export interface DoctorAssignedQueueResponseDto {
  queueDate: string;
  facilityName: string;
  shiftLabel: string;
  generatedAtUtc: string;
  doctorName: string;
  stats: DoctorAssignedQueueStatsDto;
  servicePoints: DoctorAssignedServicePointDto[];
}

export interface DoctorAssignedQueueQueryParams {
  queueDate?: string;
}

export type DoctorCallNextOutcome = 'CALLED' | 'NO_ELIGIBLE_PATIENT' | 'HOLD_FOR_SLOT';

export type DoctorCallNextSelectionReason =
  'APPOINTMENT_DUE' | 'RETURN_CONSULTATION' | 'PRIORITY' | 'SAFE_BACKFILL' | 'FIFO';

export interface DoctorCallNextRequestPayload {
  notes?: string | null;
}

export interface DoctorCallNextResponseDto {
  servicePointId: string;
  outcome: DoctorCallNextOutcome;
  message: string;
  selectionReason: DoctorCallNextSelectionReason | null;
  calledEntry: DoctorAssignedQueueEntryDto | null;
  previousStateVersion: number | null;
  stateVersion: number | null;
  updatedAtUtc: string;
  entries: DoctorAssignedQueueEntryDto[];
}
