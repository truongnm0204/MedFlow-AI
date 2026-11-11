import type {
  DiagnosticCategory,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticStaffProfile,
} from './diagnostic.types';

export type { DiagnosticStaffProfile };

export type DiagnosticOrderReviewStatusFilter = 'ALL' | DiagnosticOrderStatus;
export type DiagnosticOrderReviewCategoryFilter = 'ALL' | DiagnosticCategory;
export type DiagnosticOrderReviewPriorityFilter = 'ALL' | DiagnosticPriority;

export interface GetDiagnosticOrdersForReviewParams {
  status?: DiagnosticOrderReviewStatusFilter | string;
  category?: DiagnosticOrderReviewCategoryFilter | string;
  priority?: DiagnosticOrderReviewPriorityFilter | string;
  visitId?: number;
  visitCode?: string;
  serviceId?: number;
  roomId?: number;
  servicePointId?: number;
  pendingOnly?: boolean;
  keyword?: string;
  limit?: number;
}

export interface DiagnosticOrderPatientSummaryDto {
  patientId: number;
  patientCode: string;
  fullName: string;
  dateOfBirth?: string | null;
  age?: number | null;
  gender?: string | null;
  phone?: string | null;
  bloodType?: string | null;
  allergies?: string | null;
}

export interface DiagnosticOrderClinicalSummaryDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  reasonForVisit?: string | null;
  presentingComplaint?: string | null;
  clinicalFindings?: string | null;
  primaryDiagnosisCode?: string | null;
  primaryDiagnosisName?: string | null;
  clinicalDiagnosis?: string | null;
}

export interface DiagnosticOrderEligibilityCheckDto {
  isEligible: boolean;
  canConfirm: boolean;
  disabledReason?: string | null;
  isOrderPendingConfirmation: boolean;
  isVisitEligible: boolean;
  isPatientActive: boolean;
  isServiceActive: boolean;
  isDepartmentActive: boolean;
  isDoctorAuthorizedForService: boolean;
  isServicePointActive: boolean;
  isRoomActive: boolean;
  isQueueEntryEligible: boolean;
}

export interface DiagnosticServicePointOptionDto {
  servicePointId: number;
  name: string;
  serviceId?: number | null;
  roomId?: number | null;
  roomCode?: string | null;
  roomName?: string | null;
  roomLocation?: string | null;
  isActive: boolean;
  isRoomActive: boolean;
}

export interface DiagnosticOrderReviewItemDto {
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
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo?: string | null;
  queueNumber?: number | null;
  queueDate?: string | null;
  queueStatus?: string | null;
  presenceStatus?: string | null;
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
  canConfirm: boolean;
  disabledReason?: string | null;
  eligibility: DiagnosticOrderEligibilityCheckDto;
}

export interface DiagnosticOrderReviewSummaryStatsDto {
  totalOrders: number;
  pendingConfirmationCount: number;
  confirmedCount: number;
  inProgressCount: number;
  completedCount: number;
  cancelledCount: number;
  urgentOrStatPendingCount: number;
}

export interface DiagnosticOrderReviewListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId?: number | null;
  departmentName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  summary: DiagnosticOrderReviewSummaryStatsDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  items: DiagnosticOrderReviewItemDto[];
}

export interface DiagnosticOrderReviewDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  staffProfile: DiagnosticStaffProfile;
  order: DiagnosticOrderReviewItemDto;
  clinicalContext: DiagnosticOrderClinicalSummaryDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
}

export interface ConfirmDiagnosticOrderRequest {
  isEligibilityVerified?: boolean;
  eligibilityConfirmed?: boolean;
  servicePointId?: number | null;
  roomId?: number | null;
  priority?: DiagnosticPriority | string | null;
  instruction?: string | null;
  confirmationNote?: string | null;
  reviewNote?: string | null;
  notes?: string | null;
  patientPresent?: boolean | null;
  expectedStateVersion?: number | null;
}

export interface ConfirmDiagnosticOrdersBatchRequest {
  orderId?: number | null;
  orderCode?: string | null;
  orderIds?: number[] | null;
  orderCodes?: string[] | null;
  isEligibilityVerified?: boolean;
  eligibilityConfirmed?: boolean;
  servicePointId?: number | null;
  roomId?: number | null;
  priority?: DiagnosticPriority | string | null;
  instruction?: string | null;
  confirmationNote?: string | null;
  reviewNote?: string | null;
  notes?: string | null;
  patientPresent?: boolean | null;
  expectedStateVersion?: number | null;
}

export interface ConfirmDiagnosticOrderResponseDto {
  actionOutcome: string;
  confirmedCount: number;
  confirmedAt: string;
  confirmedByStaff: DiagnosticStaffProfile;
  order: DiagnosticOrderReviewItemDto;
  confirmedOrders: DiagnosticOrderReviewItemDto[];
}
