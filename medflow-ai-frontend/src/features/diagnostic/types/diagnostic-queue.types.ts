import type {
  DiagnosticCategory,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticQuickAction,
  DiagnosticServicePointOptionDto,
  DiagnosticStaffProfile,
} from './diagnostic.types';

export type DiagnosticQueueStatus =
  | 'RESERVED'
  | 'WAITING'
  | 'READY'
  | 'CALLED'
  | 'IN_PROGRESS'
  | 'COMPLETED'
  | 'SKIPPED'
  | 'CANCELLED';

export type DiagnosticQueueTargetStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED' | 'CANCELLED';

export type DiagnosticQueuePresenceStatus = 'PRESENT' | 'NOT_ARRIVED' | 'LEFT';

export type DiagnosticQueueClinicalPriority = 'NORMAL' | 'URGENT' | 'STAT' | 'ELDERLY' | 'PREGNANT';

export type DiagnosticQueueStatusFilter = 'ALL' | DiagnosticQueueStatus;
export type DiagnosticQueueCategoryFilter = 'ALL' | DiagnosticCategory;
export type DiagnosticQueuePriorityFilter = 'ALL' | DiagnosticQueueClinicalPriority;
export type DiagnosticQueuePresenceFilter = 'ALL' | DiagnosticQueuePresenceStatus;

export type DiagnosticQueueActionId =
  'receive' | 'call' | 'start' | 'complete' | 'missed-turn' | 'restore-turn' | 'cancel';

export interface GetDiagnosticQueueParams {
  queueDate?: string;
  servicePointId?: string;
  roomId?: string;
  serviceId?: string;
  category?: DiagnosticQueueCategoryFilter | string;
  status?: DiagnosticQueueStatusFilter | string;
  queueStatus?: DiagnosticQueueStatusFilter | string;
  orderStatus?: DiagnosticOrderStatus | 'ALL' | string;
  priority?: DiagnosticQueuePriorityFilter | string;
  presenceStatus?: DiagnosticQueuePresenceFilter | string;
  visitId?: string;
  patientId?: string;
  requiredForConclusion?: boolean;
  activeOnly?: boolean;
  keyword?: string;
  limit?: number;
}

export interface UpdateDiagnosticQueueStatusRequest {
  targetStatus?: DiagnosticQueueTargetStatus | string | null;
  status?: DiagnosticQueueTargetStatus | string | null;
  presenceStatus?: DiagnosticQueuePresenceStatus | string | null;
  servicePointId?: number | null;
  roomId?: number | null;
  reason?: string | null;
  blockedReason?: string | null;
  notes?: string | null;
  cancelLinkedOrder?: boolean | null;
  expectedStateVersion?: number | null;
}

export interface CallNextDiagnosticPatientRequest {
  servicePointId?: string | null;
  category?: DiagnosticCategory | 'ALL' | string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface DiagnosticQueueTurnActionRequest {
  servicePointId?: string | null;
  presenceStatus?: DiagnosticQueuePresenceStatus | string | null;
  reason?: string | null;
  blockedReason?: string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface CancelDiagnosticQueueEntryRequest {
  reason?: string | null;
  notes?: string | null;
  cancelLinkedOrder?: boolean;
  expectedStateVersion?: number | null;
}

export interface DiagnosticQueueItemActionDto {
  id: DiagnosticQueueActionId | string;
  label: string;
  ucCode: string;
  enabled: boolean;
  disabledReason?: string | null;
}

export interface DiagnosticQueueEntryItemDto {
  id: string;
  queueEntryId: number;
  formattedQueueEntryId: string;
  queueNumber: number;
  ticketNo: string;
  servingOrder: number;
  queueDate: string;
  queueType: string;
  status: DiagnosticQueueStatus | string;
  statusLabel: string;
  presenceStatus: DiagnosticQueuePresenceStatus | string;
  presenceStatusLabel: string;
  arrivalClass: string;
  clinicalPriority: DiagnosticQueueClinicalPriority | string;
  priority: DiagnosticPriority | string;
  priorityLabel: string;
  stateVersion: number;
  isEligible: boolean;
  ineligibleReason?: string | null;
  scheduledAt?: string | null;
  arrivedAt?: string | null;
  readyAt?: string | null;
  calledAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  blockedReason?: string | null;
  estimatedWaitMinutes: number;
  estimatedWaitLabel: string;
  orderId?: number | null;
  orderCode?: string | null;
  orderStatus: DiagnosticOrderStatus | string;
  orderStatusLabel: string;
  requiredForConclusion: boolean;
  instruction?: string | null;
  orderedAt?: string | null;
  confirmedAt?: string | null;
  confirmedByStaffId?: number | null;
  confirmedByStaffCode?: string | null;
  confirmedByStaffName?: string | null;
  visitId: number;
  visitCode: string;
  visitStatus: string;
  patientId: number;
  patientCode: string;
  patientName: string;
  patientGender: string;
  patientGenderLabel: string;
  patientDateOfBirth?: string | null;
  patientAge?: number | null;
  bloodType?: string | null;
  allergiesSummary?: string | null;
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: DiagnosticCategory;
  categoryLabel: string;
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName: string;
  specialtyId?: number | null;
  specialtyCode?: string | null;
  specialtyName: string;
  orderingDoctorId?: number | null;
  orderingDoctorCode?: string | null;
  orderingDoctorName: string;
  servicePointId?: number | null;
  formattedServicePointId: string;
  servicePointName: string;
  roomId?: number | null;
  roomCode: string;
  roomName: string;
  roomLocation?: string | null;
  hasResult: boolean;
  isResultFinalized: boolean;
  resultStatus: string;
  allowedTargetStatuses: string[];
  canReceive: boolean;
  canCall: boolean;
  canStart: boolean;
  canComplete: boolean;
  canSkip: boolean;
  canRestore: boolean;
  canCancel: boolean;
  allowedActions: DiagnosticQueueItemActionDto[];
}

export interface DiagnosticServicePointQueueBucketDto {
  servicePointId?: number | null;
  formattedServicePointId: string;
  servicePointName: string;
  roomId?: number | null;
  roomCode: string;
  roomName: string;
  roomLocation?: string | null;
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName: string;
  serviceId?: number | null;
  serviceCode?: string | null;
  serviceName?: string | null;
  category: DiagnosticCategory;
  categoryLabel: string;
  isActive: boolean;
  isRoomActive: boolean;
  capacity: number;
  currentServingTicketNo?: string | null;
  currentServingEntry?: DiagnosticQueueEntryItemDto | null;
  nextWaitingTicketNo?: string | null;
  nextWaitingEntry?: DiagnosticQueueEntryItemDto | null;
  waitingCount: number;
  readyCount: number;
  calledCount: number;
  inProgressCount: number;
  completedCount: number;
  skippedCount: number;
  cancelledCount: number;
  totalEntriesCount: number;
  estimatedWaitMinutes: number;
  entries: DiagnosticQueueEntryItemDto[];
}

export interface DiagnosticQueueSummaryStatsDto {
  totalEntries: number;
  waitingCount: number;
  readyCount: number;
  calledCount: number;
  inProgressCount: number;
  completedCount: number;
  skippedCount: number;
  cancelledCount: number;
  urgentOrStatCount: number;
  mandatoryCount: number;
  activeServicePointsCount: number;
  busyServicePointsCount: number;
  laboratoryCount: number;
  imagingCount: number;
  functionalCount: number;
}

export interface DiagnosticQueueBoardResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  facilityName: string;
  facilityCode: string;
  departmentId?: number | null;
  departmentName: string;
  queueDate: string;
  shiftLabel: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  stats: DiagnosticQueueSummaryStatsDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  servicePoints: DiagnosticServicePointQueueBucketDto[];
  entries: DiagnosticQueueEntryItemDto[];
  items: DiagnosticQueueEntryItemDto[];
  quickActions: DiagnosticQuickAction[];
}

export interface QueueStatusTransitionHistoryItemDto {
  auditLogId: number;
  queueEntryId: number;
  visitId?: number | null;
  visitCode?: string | null;
  previousStatus: string;
  targetStatus: string;
  previousPresenceStatus?: string | null;
  updatedPresenceStatus?: string | null;
  previousPriority?: string | null;
  updatedPriority?: string | null;
  previousStateVersion?: number | null;
  updatedStateVersion: number;
  reason?: string | null;
  blockedReason?: string | null;
  notes?: string | null;
  updatedByUserId?: number | null;
  updatedByStaffCode?: string | null;
  updatedByName?: string | null;
  updatedAtUtc: string;
}

export interface DiagnosticQueueEntryDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  entry: DiagnosticQueueEntryItemDto;
  clinicalContext: DiagnosticOrderClinicalSummaryDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  statusHistory: QueueStatusTransitionHistoryItemDto[];
}

export interface ManageDiagnosticQueueActionResponseDto {
  actionOutcome: string;
  action: string;
  message: string;
  queueEntryId: number;
  formattedQueueEntryId: string;
  ticketNo: string;
  queueNumber: number;
  orderId?: number | null;
  orderCode?: string | null;
  previousStatus: string;
  currentStatus: string;
  previousOrderStatus?: string | null;
  orderStatus?: string | null;
  previousPresenceStatus: string;
  presenceStatus: string;
  previousServingOrder: number;
  servingOrder: number;
  previousStateVersion: number;
  stateVersion: number;
  blockedReason?: string | null;
  reason?: string | null;
  notes?: string | null;
  updatedAtUtc: string;
  updatedByStaff: DiagnosticStaffProfile;
  entry: DiagnosticQueueEntryItemDto;
  servicePointBucket?: DiagnosticServicePointQueueBucketDto | null;
}

export interface CallNextDiagnosticPatientResponseDto {
  servicePointId?: number | null;
  formattedServicePointId: string;
  servicePointName: string;
  outcome: 'CALLED' | 'NO_ELIGIBLE_PATIENT' | string;
  message: string;
  selectionReason?: string | null;
  calledEntry?: DiagnosticQueueEntryItemDto | null;
  previousStateVersion?: number | null;
  stateVersion?: number | null;
  updatedAtUtc: string;
  calledByStaff: DiagnosticStaffProfile;
  servicePointBucket: DiagnosticServicePointQueueBucketDto;
  entries: DiagnosticQueueEntryItemDto[];
}
