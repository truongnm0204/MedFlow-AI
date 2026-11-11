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
export type DiagnosticClinicalPriority = 'NORMAL' | 'ELDERLY' | 'PREGNANT';

export type DiagnosticPatientReceiveStatusFilter = 'ALL' | DiagnosticOrderStatus;
export type DiagnosticPatientReceiveQueueStatusFilter = 'ALL' | DiagnosticQueueStatus;
export type DiagnosticPatientReceiveCategoryFilter = 'ALL' | DiagnosticCategory;
export type DiagnosticPatientReceivePriorityFilter = 'ALL' | DiagnosticPriority;

export interface GetDiagnosticOrdersForReceiveParams {
  status?: DiagnosticPatientReceiveStatusFilter | string;
  queueStatus?: DiagnosticPatientReceiveQueueStatusFilter | string;
  category?: DiagnosticPatientReceiveCategoryFilter | string;
  priority?: DiagnosticPatientReceivePriorityFilter | string;
  visitId?: number;
  visitCode?: string;
  serviceId?: number;
  roomId?: number;
  servicePointId?: number;
  readyToReceiveOnly?: boolean;
  keyword?: string;
  limit?: number;
}

export interface DiagnosticPatientReceiveEligibilityDto {
  isEligible: boolean;
  canReceive: boolean;
  disabledReason?: string | null;
  isOrderConfirmed: boolean;
  isQueueWaitingForReception: boolean;
  isVisitEligible: boolean;
  isPatientActive: boolean;
  isServiceActive: boolean;
  isDepartmentActive: boolean;
  isServicePointActive: boolean;
  isRoomActive: boolean;
  hasQueueManagePermission: boolean;
}

export interface DiagnosticPatientReceiveItemDto {
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
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo?: string | null;
  queueNumber?: number | null;
  queueDate?: string | null;
  queueStatus?: DiagnosticQueueStatus | string | null;
  presenceStatus?: string | null;
  manualPriority?: DiagnosticClinicalPriority | string | null;
  effectiveClinicalPriority?: DiagnosticClinicalPriority | string | null;
  arrivedAt?: string | null;
  readyAt?: string | null;
  stateVersion: number;
  servicePointId?: number | null;
  servicePointName?: string | null;
  roomId?: number | null;
  roomCode?: string | null;
  roomName?: string | null;
  roomLocation?: string | null;
  isAdditionalOrder: boolean;
  orderSequenceNo: number;
  followUpToOrderId?: number | null;
  followUpToOrderCode?: string | null;
  additionalOrderReason?: string | null;
  canReceive: boolean;
  disabledReason?: string | null;
  eligibility: DiagnosticPatientReceiveEligibilityDto;
}

export interface DiagnosticPatientReceiveSummaryStatsDto {
  totalOrders: number;
  pendingConfirmationCount: number;
  waitingReceptionCount: number;
  readyInQueueCount: number;
  inProgressCount: number;
  completedCount: number;
  cancelledCount: number;
  urgentOrStatWaitingCount: number;
}

export interface DiagnosticPatientReceiveListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId?: number | null;
  departmentName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  summary: DiagnosticPatientReceiveSummaryStatsDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  items: DiagnosticPatientReceiveItemDto[];
}

export interface DiagnosticPatientReceiveDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  order: DiagnosticPatientReceiveItemDto;
  clinicalContext: DiagnosticOrderClinicalSummaryDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
}

export interface ReceiveDiagnosticPatientRequest {
  isPatientPresent?: boolean;
  patientPresent?: boolean;
  isIdentityVerified?: boolean;
  identityVerified?: boolean;
  isPreparationVerified?: boolean;
  servicePointId?: number | null;
  roomId?: number | null;
  priority?: DiagnosticPriority | string | null;
  manualPriority?: DiagnosticClinicalPriority | string | null;
  clinicalPriority?: DiagnosticClinicalPriority | string | null;
  instruction?: string | null;
  receptionNote?: string | null;
  receiveNote?: string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface ReceiveDiagnosticPatientsBatchRequest {
  orderId?: number | null;
  orderCode?: string | null;
  orderIds?: number[] | null;
  orderCodes?: string[] | null;
  visitId?: number | null;
  visitCode?: string | null;
  isPatientPresent?: boolean;
  patientPresent?: boolean;
  isIdentityVerified?: boolean;
  identityVerified?: boolean;
  isPreparationVerified?: boolean;
  servicePointId?: number | null;
  roomId?: number | null;
  priority?: DiagnosticPriority | string | null;
  manualPriority?: DiagnosticClinicalPriority | string | null;
  clinicalPriority?: DiagnosticClinicalPriority | string | null;
  instruction?: string | null;
  receptionNote?: string | null;
  receiveNote?: string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface ReceiveDiagnosticPatientResponseDto {
  actionOutcome: string;
  receivedCount: number;
  receivedAt: string;
  receivedByStaff: DiagnosticStaffProfile;
  order: DiagnosticPatientReceiveItemDto;
  receivedOrders: DiagnosticPatientReceiveItemDto[];
}
