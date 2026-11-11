// Types cho màn Cập nhật trạng thái thực hiện kỹ thuật CLS (M3-131 / UC42).
// Đồng bộ 1:1 với UpdateDiagnosticServiceStatusDtos.cs (ADR 0034).
import type {
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticOrderPatientSummaryDto,
  DiagnosticServicePointOptionDto,
} from './diagnostic-order-review.types';
import type { DiagnosticQueueStatus } from './diagnostic-queue.types';
import type {
  DiagnosticCategory,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticStaffProfile,
} from './diagnostic.types';

// BE targetStatus chấp nhận IN_PROGRESS/COMPLETED (thực hiện) và
// CONFIRMED/READY/WAITING (tạm dừng về hàng đợi) + CANCELLED — ADR 0034 §3.
export type DiagnosticExecutionTargetStatus =
  'IN_PROGRESS' | 'COMPLETED' | 'CONFIRMED' | 'READY' | 'WAITING' | 'CANCELLED';

export type DiagnosticExecutionStatusFilter = 'ALL' | DiagnosticOrderStatus;
export type DiagnosticExecutionQueueStatusFilter = 'ALL' | DiagnosticQueueStatus;
export type DiagnosticExecutionCategoryFilter = 'ALL' | DiagnosticCategory;
export type DiagnosticExecutionPriorityFilter = 'ALL' | DiagnosticPriority;

export interface GetDiagnosticExecutionsParams {
  status?: DiagnosticExecutionStatusFilter | string;
  queueStatus?: DiagnosticExecutionQueueStatusFilter | string;
  category?: DiagnosticExecutionCategoryFilter | string;
  priority?: DiagnosticExecutionPriorityFilter | string;
  visitId?: number;
  visitCode?: string;
  serviceId?: number;
  roomId?: number;
  servicePointId?: number;
  requiredForConclusion?: boolean;
  activeOnly?: boolean;
  readyToExecuteOnly?: boolean;
  keyword?: string;
  limit?: number;
}

export interface UpdateDiagnosticServiceStatusRequest {
  targetStatus?: DiagnosticExecutionTargetStatus | string | null;
  status?: DiagnosticExecutionTargetStatus | string | null;
  presenceStatus?: string | null;
  servicePointId?: number | null;
  roomId?: number | null;
  specimenCode?: string | null;
  sampleCode?: string | null;
  specimenType?: string | null;
  modality?: string | null;
  equipmentCode?: string | null;
  deviceName?: string | null;
  executionNote?: string | null;
  technicalNote?: string | null;
  notes?: string | null;
  instruction?: string | null;
  reason?: string | null;
  blockedReason?: string | null;
  expectedStateVersion?: number | null;
}

export interface UpdateDiagnosticServiceStatusBatchRequest {
  orderId?: number | null;
  orderCode?: string | null;
  orderIds?: number[] | null;
  orderCodes?: string[] | null;
  visitId?: number | null;
  visitCode?: string | null;
  targetStatus?: DiagnosticExecutionTargetStatus | string | null;
  status?: DiagnosticExecutionTargetStatus | string | null;
  presenceStatus?: string | null;
  servicePointId?: number | null;
  roomId?: number | null;
  specimenCode?: string | null;
  sampleCode?: string | null;
  specimenType?: string | null;
  modality?: string | null;
  equipmentCode?: string | null;
  deviceName?: string | null;
  executionNote?: string | null;
  technicalNote?: string | null;
  notes?: string | null;
  instruction?: string | null;
  reason?: string | null;
  blockedReason?: string | null;
  expectedStateVersion?: number | null;
}

export interface DiagnosticServiceExecutionEligibilityDto {
  isEligible: boolean;
  canUpdateStatus: boolean;
  canStartExecution: boolean;
  canCompleteExecution: boolean;
  canPauseExecution: boolean;
  canCancelExecution: boolean;
  disabledReason?: string | null;
  allowedTargetStatuses: string[];
  isOrderConfirmedOrInProgress: boolean;
  isQueueEligible: boolean;
  isVisitActive: boolean;
  isPatientActive: boolean;
  isServiceActive: boolean;
  isDepartmentActive: boolean;
  isServicePointActive: boolean;
  isRoomActive: boolean;
  isResultNotFinalized: boolean;
  hasExecutionPermission: boolean;
}

export interface DiagnosticServiceExecutionHistoryItemDto {
  auditLogId: number;
  orderId: number;
  orderCode: string;
  queueEntryId?: number | null;
  action: string;
  actionLabel: string;
  previousStatus: string;
  currentStatus: string;
  previousQueueStatus?: string | null;
  currentQueueStatus?: string | null;
  previousStateVersion?: number | null;
  stateVersion: number;
  specimenCode?: string | null;
  specimenType?: string | null;
  equipmentCode?: string | null;
  executionNote?: string | null;
  reason?: string | null;
  actorUserId?: number | null;
  performedByStaffId?: number | null;
  performedByStaffCode?: string | null;
  performedByStaffName?: string | null;
  occurredAtUtc: string;
}

export interface DiagnosticServiceExecutionItemDto {
  orderId: number;
  orderCode: string;
  visitId: number;
  visitCode: string;
  visitStatus: string;
  patient: DiagnosticOrderPatientSummaryDto;
  orderingDoctorId?: number | null;
  orderingDoctorCode?: string | null;
  orderingDoctorName: string;
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: DiagnosticCategory | string;
  categoryLabel: string;
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName: string;
  specialtyId?: number | null;
  specialtyCode?: string | null;
  specialtyName: string;
  unitPrice: number;
  requiresResultVerification: boolean;
  priority: DiagnosticPriority | string;
  priorityLabel: string;
  status: DiagnosticOrderStatus | string;
  statusLabel: string;
  requiredForConclusion: boolean;
  instruction?: string | null;
  orderedAt: string;
  confirmedAt?: string | null;
  confirmedByStaffId?: number | null;
  confirmedByStaffCode?: string | null;
  confirmedByStaffName?: string | null;
  confirmationNote?: string | null;
  receivedAt?: string | null;
  receivedByStaffId?: number | null;
  receivedByStaffCode?: string | null;
  receivedByStaffName?: string | null;
  receptionNote?: string | null;
  startedAt?: string | null;
  startedByStaffId?: number | null;
  startedByStaffCode?: string | null;
  startedByStaffName?: string | null;
  completedAt?: string | null;
  completedByStaffId?: number | null;
  completedByStaffCode?: string | null;
  completedByStaffName?: string | null;
  specimenCode?: string | null;
  specimenType?: string | null;
  equipmentCode?: string | null;
  executionNote?: string | null;
  lastTransitionReason?: string | null;
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo?: string | null;
  queueNumber?: number | null;
  queueDate?: string | null;
  queueStatus?: DiagnosticQueueStatus | string | null;
  queueStatusLabel?: string | null;
  presenceStatus?: string | null;
  manualPriority?: string | null;
  effectiveClinicalPriority?: string | null;
  arrivedAt?: string | null;
  readyAt?: string | null;
  calledAt?: string | null;
  stateVersion: number;
  servicePointId?: number | null;
  formattedServicePointId?: string | null;
  servicePointName?: string | null;
  roomId?: number | null;
  roomCode?: string | null;
  roomName?: string | null;
  roomLocation?: string | null;
  hasResult: boolean;
  isResultFinalized: boolean;
  resultStatus: string;
  isAdditionalOrder: boolean;
  orderSequenceNo: number;
  followUpToOrderId?: number | null;
  followUpToOrderCode?: string | null;
  additionalOrderReason?: string | null;
  canUpdateStatus: boolean;
  canStartExecution: boolean;
  canCompleteExecution: boolean;
  canPauseExecution: boolean;
  canCancelExecution: boolean;
  canEnterResult: boolean;
  disabledReason?: string | null;
  allowedTargetStatuses: string[];
  eligibility: DiagnosticServiceExecutionEligibilityDto;
}

export interface DiagnosticServiceExecutionSummaryStatsDto {
  totalOrders: number;
  pendingConfirmationCount: number;
  waitingExecutionCount: number;
  readyToStartCount: number;
  inProgressCount: number;
  completedCount: number;
  cancelledCount: number;
  urgentOrStatCount: number;
  mandatoryCount: number;
  laboratoryCount: number;
  imagingCount: number;
  functionalCount: number;
}

export interface DiagnosticServiceExecutionListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  facilityName: string;
  facilityCode: string;
  departmentId?: number | null;
  departmentName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  summary: DiagnosticServiceExecutionSummaryStatsDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  items: DiagnosticServiceExecutionItemDto[];
}

export interface DiagnosticServiceExecutionDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  order: DiagnosticServiceExecutionItemDto;
  clinicalContext: DiagnosticOrderClinicalSummaryDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  executionHistory: DiagnosticServiceExecutionHistoryItemDto[];
}

export interface UpdateDiagnosticServiceStatusResponseDto {
  actionOutcome: string;
  action: string;
  message: string;
  updatedCount: number;
  orderId: number;
  orderCode: string;
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo?: string | null;
  previousStatus: string;
  currentStatus: string;
  previousQueueStatus?: string | null;
  currentQueueStatus?: string | null;
  previousStateVersion: number;
  stateVersion: number;
  specimenCode?: string | null;
  specimenType?: string | null;
  equipmentCode?: string | null;
  executionNote?: string | null;
  reason?: string | null;
  updatedAtUtc: string;
  updatedByStaff: DiagnosticStaffProfile;
  order: DiagnosticServiceExecutionItemDto;
  updatedOrders: DiagnosticServiceExecutionItemDto[];
}
