// Types cho màn Nhập & trả kết quả cận lâm sàng (UC43 — M3-133).
// Đồng bộ với DTO response của endpoint GET/POST /api/v1/diagnostic/results
// (`EnterDiagnosticResultDtos.cs` — BE #134).

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

export type DiagnosticResultStatus =
  'NONE' | 'DRAFT' | 'PRELIMINARY' | 'PENDING_VERIFY' | 'FINALIZED' | 'VERIFIED';

export type DiagnosticResultEntryStatusFilter = 'ALL' | DiagnosticOrderStatus;
export type DiagnosticResultEntryResultStatusFilter = 'ALL' | DiagnosticResultStatus;
export type DiagnosticResultEntryQueueStatusFilter = 'ALL' | DiagnosticQueueStatus;
export type DiagnosticResultEntryCategoryFilter = 'ALL' | DiagnosticCategory;
export type DiagnosticResultEntryPriorityFilter = 'ALL' | DiagnosticPriority;

export interface GetDiagnosticOrdersForResultEntryParams {
  status?: DiagnosticResultEntryStatusFilter | string;
  resultStatus?: DiagnosticResultEntryResultStatusFilter | string;
  queueStatus?: DiagnosticResultEntryQueueStatusFilter | string;
  category?: DiagnosticResultEntryCategoryFilter | string;
  priority?: DiagnosticResultEntryPriorityFilter | string;
  visitId?: number;
  visitCode?: string;
  serviceId?: number;
  roomId?: number;
  servicePointId?: number;
  readyForResultEntryOnly?: boolean;
  pendingFinalizationOnly?: boolean;
  keyword?: string;
  limit?: number;
}

export interface EnterDiagnosticResultRequest {
  orderId?: number | null;
  orderCode?: string | null;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  status?: string | null;
  isFinalized?: boolean | null;
  isVerified?: boolean | null;
  correctionReason?: string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

/** Body của POST .../result/amend. Không gửi status, notes, isFinalized. */
export interface UpdateDiagnosticResultRequest {
  orderId?: number | null;
  orderCode?: string | null;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  correctionReason?: string | null;
  amendmentReason?: string | null;
  expectedStateVersion?: number | null;
}

export interface DiagnosticResultEntryEligibilityDto {
  isEligible: boolean;
  canEnterResult: boolean;
  canFinalizeResult: boolean;
  disabledReason?: string | null;
  isOrderExecutionStartedOrCompleted: boolean;
  isResultAlreadyFinalized: boolean;
  isVisitEligible: boolean;
  isMedicalRecordOpen: boolean;
  isPatientActive: boolean;
  isServiceActive: boolean;
  isDepartmentActive: boolean;
  isServicePointActive: boolean;
  isRoomActive: boolean;
  hasResultWritePermission: boolean;
  canAmendResult: boolean;
  amendDisabledReason?: string | null;
}

export interface DiagnosticResultVersionItemDto {
  versionId: number;
  resultId: number;
  versionNo: number;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  isFinalized: boolean;
  enteredByStaffId?: number | null;
  enteredByStaffCode?: string | null;
  enteredByStaffName?: string | null;
  enteredAt?: string | null;
  verifiedByStaffId?: number | null;
  verifiedByStaffCode?: string | null;
  verifiedByStaffName?: string | null;
  verifiedAt?: string | null;
  correctionReason?: string | null;
}

export interface DiagnosticResultHistoryItemDto {
  auditLogId: number;
  orderId: number;
  orderCode: string;
  resultId?: number | null;
  versionNo?: number | null;
  action: string;
  resultStatus: string;
  isFinalized: boolean;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  reason?: string | null;
  actorUserId?: number | null;
  actorStaffCode?: string | null;
  actorStaffName?: string | null;
  occurredAtUtc: string;
}

export interface DiagnosticResultEntryItemDto {
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
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName: string;
  specialtyId?: number | null;
  specialtyCode?: string | null;
  specialtyName: string;
  unitPrice: number;
  requiresResultVerification: boolean;
  priority: DiagnosticPriority | string;
  status: DiagnosticOrderStatus | string;
  orderStatus: string;
  requiredForConclusion: boolean;
  instruction?: string | null;
  orderedAt: string;
  confirmedAt?: string | null;
  confirmedByStaffId?: number | null;
  confirmedByStaffCode?: string | null;
  confirmedByStaffName?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo?: string | null;
  queueNumber?: number | null;
  queueDate?: string | null;
  queueStatus?: DiagnosticQueueStatus | string | null;
  presenceStatus?: string | null;
  stateVersion: number;
  servicePointId?: number | null;
  servicePointName?: string | null;
  roomId?: number | null;
  roomCode?: string | null;
  roomName?: string | null;
  roomLocation?: string | null;
  hasResult: boolean;
  isResultReady: boolean;
  isResultFinalized: boolean;
  resultId?: number | null;
  resultStatus: string;
  resultFinalizedAt?: string | null;
  finalizedByStaffId?: number | null;
  finalizedByStaffCode?: string | null;
  finalizedByStaffName?: string | null;
  latestVersionNo?: number | null;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  enteredByStaffId?: number | null;
  enteredByStaffCode?: string | null;
  enteredByStaffName?: string | null;
  enteredAt?: string | null;
  verifiedByStaffId?: number | null;
  verifiedByStaffCode?: string | null;
  verifiedByStaffName?: string | null;
  verifiedAt?: string | null;
  correctionReason?: string | null;
  isAdditionalOrder: boolean;
  orderSequenceNo: number;
  followUpToOrderId?: number | null;
  followUpToOrderCode?: string | null;
  additionalOrderReason?: string | null;
  canEnterResult: boolean;
  canFinalizeResult: boolean;
  disabledReason?: string | null;
  eligibility: DiagnosticResultEntryEligibilityDto;
  versions: DiagnosticResultVersionItemDto[];
  canAmendResult: boolean;
  amendDisabledReason?: string | null;
}

export interface DiagnosticResultEntrySummaryStatsDto {
  totalOrders: number;
  readyForResultEntryCount: number;
  inProgressExecutionCount: number;
  draftOrPreliminaryCount: number;
  pendingVerificationCount: number;
  finalizedCount: number;
  cancelledCount: number;
  urgentOrStatPendingResultCount: number;
}

export interface DiagnosticResultEntryListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId?: number | null;
  departmentName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  summary: DiagnosticResultEntrySummaryStatsDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  items: DiagnosticResultEntryItemDto[];
}

export interface DiagnosticResultEntryDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  order: DiagnosticResultEntryItemDto;
  clinicalContext: DiagnosticOrderClinicalSummaryDto;
  versions: DiagnosticResultVersionItemDto[];
  statusHistory: DiagnosticResultHistoryItemDto[];
  availableServicePoints: DiagnosticServicePointOptionDto[];
}

export interface EnterDiagnosticResultResponseDto {
  actionOutcome: string;
  message: string;
  orderId: number;
  orderCode: string;
  resultId: number;
  versionNo: number;
  resultStatus: string;
  isFinalized: boolean;
  previousOrderStatus: string;
  orderStatus: string;
  previousQueueStatus?: string | null;
  queueStatus?: string | null;
  previousStateVersion: number;
  stateVersion: number;
  enteredAt: string;
  finalizedAt?: string | null;
  enteredByStaff: DiagnosticStaffProfile;
  order: DiagnosticResultEntryItemDto;
  latestVersion: DiagnosticResultVersionItemDto;
  versions: DiagnosticResultVersionItemDto[];
}
