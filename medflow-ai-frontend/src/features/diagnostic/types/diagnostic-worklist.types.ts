import type {
  DiagnosticCategory,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticQuickAction,
  DiagnosticStaffProfile,
} from './diagnostic.types';

export type DiagnosticWorklistQueueStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED' | 'CANCELLED';

export type DiagnosticWorklistResultStatus =
  'PENDING' | 'DRAFT' | 'PRELIMINARY' | 'PENDING_VERIFY' | 'FINALIZED' | 'VERIFIED' | 'CANCELLED';

export type DiagnosticWorklistActionId =
  'confirm-order' | 'receive-patient' | 'update-execution' | 'enter-result' | 'view-result';

export interface DiagnosticWorklistActionDto {
  id: DiagnosticWorklistActionId | string;
  label: string;
  ucCode: string;
  enabled: boolean;
  disabledReason?: string | null;
}

export interface DiagnosticWorklistItemDto {
  id: string;
  orderId: number;
  orderCode: string;
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
  unitPrice: number;
  RequiresResultVerification?: boolean;
  requiresResultVerification: boolean;
  orderingDoctorId?: number | null;
  orderingDoctorCode?: string | null;
  orderingDoctor: string;
  queueEntryId?: number | null;
  formattedQueueEntryId?: string | null;
  ticketNo: string;
  queueNumber?: number | null;
  queueDate?: string | null;
  queueStatus?: DiagnosticWorklistQueueStatus | string | null;
  presenceStatus?: string | null;
  clinicalPriority?: string | null;
  stateVersion: number;
  servicePointId?: number | null;
  formattedServicePointId?: string | null;
  servicePointName?: string | null;
  roomId?: number | null;
  room: string;
  roomCode?: string | null;
  roomName?: string | null;
  roomLocation?: string | null;
  priority: DiagnosticPriority;
  priorityLabel: string;
  status: DiagnosticOrderStatus;
  orderStatus: string;
  statusLabel: string;
  requiredForConclusion: boolean;
  instruction?: string | null;
  reasonForVisit?: string | null;
  presentingComplaint?: string | null;
  clinicalFindings?: string | null;
  orderedAt: string;
  orderedAtTimestamp: string;
  confirmedAt?: string | null;
  confirmedByStaffId?: number | null;
  confirmedByStaffCode?: string | null;
  confirmedByStaffName?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  hasResult: boolean;
  isResultReady: boolean;
  isResultFinalized: boolean;
  resultId?: number | null;
  resultStatus: DiagnosticWorklistResultStatus | string;
  resultFinalizedAt?: string | null;
  resultVersionNo?: number | null;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  performedByStaffName?: string | null;
  verifiedByStaffName?: string | null;
  isAdditionalOrder: boolean;
  orderSequenceNo: number;
  followUpToOrderId?: number | null;
  followUpToOrderCode?: string | null;
  additionalOrderReason?: string | null;
  canConfirmOrder: boolean;
  confirmDisabledReason?: string | null;
  canReceivePatient: boolean;
  canUpdateExecutionStatus: boolean;
  canEnterResult: boolean;
  canViewResult: boolean;
  allowedActions: DiagnosticWorklistActionDto[];
}

export interface DiagnosticServicePointWorklistDto {
  servicePointId?: number | null;
  formattedServicePointId: string;
  servicePointName: string;
  roomId?: number | null;
  roomCode: string;
  roomName: string;
  roomLocation?: string | null;
  serviceId?: number | null;
  serviceCode?: string | null;
  serviceName?: string | null;
  category: DiagnosticCategory;
  isActive: boolean;
  currentServingTicketNo?: string | null;
  nextWaitingTicketNo?: string | null;
  pendingOrdersCount: number;
  confirmedCount: number;
  inProgressCount: number;
  completedCount: number;
  cancelledCount: number;
  totalOrdersCount: number;
}

export interface DiagnosticWorklistStatsDto {
  totalOrders: number;
  pendingOrders: number;
  confirmedOrders: number;
  inQueue: number;
  inExecution: number;
  completedOrders: number;
  completedToday: number;
  cancelledOrders: number;
  urgentOrders: number;
  statOrders: number;
  mandatoryOrders: number;
  pendingResultVerificationCount: number;
  laboratoryCount: number;
  imagingCount: number;
  functionalCount: number;
}

export interface DiagnosticWorklistQueryParams {
  limit?: number;
  queueLimit?: number;
  page?: number;
  pageSize?: number;
  category?: DiagnosticCategory | 'ALL';
  status?: DiagnosticOrderStatus | 'ALL';
  queueStatus?: DiagnosticWorklistQueueStatus | 'ALL';
  resultStatus?: DiagnosticWorklistResultStatus | 'ALL';
  priority?: DiagnosticPriority | 'ALL';
  orderDate?: string;
  queueDate?: string;
  servicePointId?: string;
  roomId?: string;
  serviceId?: string;
  visitId?: string;
  patientId?: string;
  doctorId?: string;
  requiredForConclusion?: boolean;
  keyword?: string;
}

export interface DiagnosticWorklistResponseDto {
  facilityName: string;
  facilityCode: string;
  departmentName: string;
  shiftLabel: string;
  effectiveDate?: string | null;
  generatedAtUtc: string;
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  stats: DiagnosticWorklistStatsDto;
  servicePoints: DiagnosticServicePointWorklistDto[];
  items: DiagnosticWorklistItemDto[];
  orders: DiagnosticWorklistItemDto[];
  queue: DiagnosticWorklistItemDto[];
  quickActions: DiagnosticQuickAction[];
  staffProfile: DiagnosticStaffProfile;
}
