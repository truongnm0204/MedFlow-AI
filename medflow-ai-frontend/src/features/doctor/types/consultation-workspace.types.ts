import type {
  DoctorQueueArrivalClass,
  DoctorQueueGroup,
  DoctorQueueIneligibleReason,
  DoctorQueuePresenceStatus,
} from './doctor-assigned-queue.types';
import type { AssignedQueueType, VisitPriority } from './doctor.types';

export type ConsultationActionOutcome =
  | 'VIEWED'
  | 'OPENED'
  | 'ALREADY_IN_PROGRESS'
  | 'CLINICAL_EXAMINATION_RECORDED'
  | 'DIAGNOSTIC_ORDER_CREATED'
  | 'DIAGNOSIS_RECORDED'
  | 'VISIT_COMPLETED';

export type ConsultationWorkspaceState =
  'WAITING' | 'CALLED' | 'IN_PROGRESS' | 'AWAITING_RESULTS' | 'RETURN_CONSULTATION' | 'COMPLETED';

export type ConsultationMedicalRecordStatus = 'NOT_CREATED' | 'DRAFT' | 'FINALIZED';

export type ConsultationDiagnosisStatus = 'PRELIMINARY' | 'CONFIRMED';

export type DiagnosticOrderPriority = 'NORMAL' | 'URGENT' | 'STAT';

export type DiagnosticServiceCategory = 'LABORATORY' | 'IMAGING' | 'FUNCTIONAL';

export type DiagnosticOrderItemStatus =
  'ORDERED' | 'CONFIRMED' | 'IN_PROGRESS' | 'COMPLETED' | 'CANCELLED';
export type ConsultationWorkspaceActionId =
  | 'open-consultation'
  | 'view-medical-history'
  | 'record-clinical-examination'
  | 'create-diagnostic-order'
  | 'create-additional-diagnostic-order'
  | 'review-diagnostic-results'
  | 'record-diagnosis'
  | 'create-prescription'
  | 'complete-visit'
  | (string & {});

export interface ConsultationVisitSummaryDto {
  visitId: number;
  visitCode: string;
  status: string;
  statusLabel: string;
  visitType: 'SCHEDULED' | 'WALK_IN' | (string & {});
  visitTypeLabel: string;
  reasonForVisit: string | null;
  appointmentId: number | null;
  appointmentCode: string | null;
  scheduledStartAt: string | null;
  scheduledEndAt: string | null;
  checkedInAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  roomLocation: string | null;
  assignedDoctorId: number;
  assignedDoctorCode: string;
  assignedDoctorName: string;
  assignedDoctorTitle: string | null;
  serviceId: number | null;
  serviceCode: string | null;
  serviceName: string | null;
}

export interface ConsultationPatientSummaryDto {
  patientId: number;
  patientCode: string;
  fullName: string;
  dateOfBirth: string | null;
  birthYear: number | null;
  age: number | null;
  gender: string | null;
  genderLabel: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  bloodType: string | null;
  allergiesSummary: string | null;
}

export interface ConsultationQueueContextDto {
  queueEntryId: number;
  formattedQueueEntryId: string;
  ticketNo: string;
  queueNumber: number;
  queueDate: string;
  queueType: AssignedQueueType | (string & {});
  status: string;
  queueGroup: DoctorQueueGroup;
  ineligibleReason: DoctorQueueIneligibleReason | null;
  priority: VisitPriority;
  presenceStatus: DoctorQueuePresenceStatus;
  arrivalClass: DoctorQueueArrivalClass;
  servingOrder: number;
  isEligible: boolean;
  isAppointmentProtected: boolean;
  holdReason: string | null;
  blockedReason: string | null;
  stateVersion: number;
  servicePointId: string;
  servicePointName: string;
  scheduledAt: string | null;
  arrivedAt: string | null;
  readyAt: string | null;
  calledAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ConsultationVitalSignsDto {
  bpSystolicMmhg: number | null;
  bpDiastolicMmhg: number | null;
  bloodPressureLabel: string | null;
  pulseBpm: number | null;
  temperatureC: number | null;
  spo2Percent: number | null;
  heightCm: number | null;
  weightKg: number | null;
  bmi: number | null;
}

export interface ConsultationDiagnosisItemDto {
  id: number;
  icd10CodeId: number | null;
  code: string;
  name: string;
  isPrimary: boolean;
  clinicalDiagnosis: string | null;
  status: ConsultationDiagnosisStatus | (string & {});
  confirmedAt: string | null;
  recordedAt: string | null;
}

export interface ConsultationDiagnosticSummaryDto {
  totalQueueEntries: number;
  openQueueEntries: number;
  completedQueueEntries: number;
  allMandatoryResultsReady: boolean;
  finalResultRequired: boolean;
}

export interface ConsultationPrescriptionSummaryDto {
  totalPrescriptions: number;
  latestPrescriptionCode: string | null;
  latestPrescriptionStatus: string | null;
  latestIssuedAt: string | null;
  itemCount: number;
}

export interface ConsultationDiagnosticOrderDto {
  orderId: number;
  orderCode: string;
  visitId: number;
  visitCode: string;
  orderingDoctorId: number;
  orderingDoctorCode: string;
  orderingDoctorName: string;
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: DiagnosticServiceCategory | (string & {});
  unitPrice: number;
  requiresResultVerification: boolean;
  queueEntryId: number | null;
  formattedQueueEntryId: string | null;
  ticketNo: string | null;
  queueNumber: number | null;
  queueStatus: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  priority: DiagnosticOrderPriority | (string & {});
  status: DiagnosticOrderItemStatus | (string & {});
  requiredForConclusion: boolean;
  instruction: string | null;
  orderedAt: string;
  confirmedAt: string | null;
  isResultFinalized: boolean;
  resultId?: number | null;
  resultStatus?: string | null;
  resultFinalizedAt?: string | null;
  resultVersionNo?: number | null;
  resultText?: string | null;
  conclusion?: string | null;
  reportFileUrl?: string | null;
  performedByStaffName?: string | null;
  verifiedByStaffName?: string | null;
}

export interface DiagnosticServiceCatalogItemDto {
  serviceId: number;
  serviceCode: string;
  name: string;
  description: string | null;
  category: DiagnosticServiceCategory | (string & {});
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string;
  unitPrice: number;
  requiresResultVerification: boolean;
  isDoctorAuthorized: boolean;
  servicePointId: number | null;
  servicePointName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  hasActiveOrderInVisit: boolean;
}

export interface DiagnosticOrderCatalogResponseDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  workspaceState: ConsultationWorkspaceState | (string & {});
  canCreateDiagnosticOrder: boolean;
  disabledReason: string | null;
  generatedAtUtc: string;
  diagnosticSummary: ConsultationDiagnosticSummaryDto;
  services: DiagnosticServiceCatalogItemDto[];
  existingOrders: ConsultationDiagnosticOrderDto[];
}

export interface ConsultationClinicalContextDto {
  medicalRecordId: number | null;
  medicalRecordStatus: ConsultationMedicalRecordStatus | (string & {});
  finalizedAt: string | null;
  currentRevisionNo: number | null;
  isConfirmed: boolean;
  lastRecordedAt: string | null;
  presentingComplaint: string | null;
  symptomHistory: string | null;
  relevantHistoryAllergies: string | null;
  vitalSigns: ConsultationVitalSignsDto;
  clinicalFindings: string | null;
  clinicalConclusion: string | null;
  amendmentReason: string | null;
  diagnoses: ConsultationDiagnosisItemDto[];
  diagnosticSummary: ConsultationDiagnosticSummaryDto;
  prescriptionSummary: ConsultationPrescriptionSummaryDto;
  diagnosticOrders?: ConsultationDiagnosticOrderDto[] | null;
}

export interface ConsultationWorkspaceActionDto {
  id: ConsultationWorkspaceActionId;
  label: string;
  ucCode: string;
  enabled: boolean;
  disabledReason: string | null;
}

export interface ConsultationWorkspaceResponseDto {
  actionOutcome: ConsultationActionOutcome | (string & {});
  workspaceState: ConsultationWorkspaceState | (string & {});
  canEditClinical: boolean;
  readOnlyReason: string | null;
  generatedAtUtc: string;
  visit: ConsultationVisitSummaryDto;
  patient: ConsultationPatientSummaryDto;
  queue: ConsultationQueueContextDto | null;
  clinicalContext: ConsultationClinicalContextDto;
  allowedActions: ConsultationWorkspaceActionDto[];
  createdDiagnosticOrders?: ConsultationDiagnosticOrderDto[] | null;
}

export interface OpenConsultationWorkspaceRequest {
  expectedStateVersion?: number | null;
  notes?: string | null;
}

export interface RecordClinicalVitalSignsRequest {
  bpSystolicMmhg?: number | null;
  bpDiastolicMmhg?: number | null;
  pulseBpm?: number | null;
  temperatureC?: number | null;
  spo2Percent?: number | null;
  heightCm?: number | null;
  weightKg?: number | null;
}

export interface RecordClinicalExaminationRequest {
  presentingComplaint?: string | null;
  symptomHistory?: string | null;
  relevantHistoryAllergies?: string | null;
  vitalSigns?: RecordClinicalVitalSignsRequest | null;
  bpSystolicMmhg?: number | null;
  bpDiastolicMmhg?: number | null;
  pulseBpm?: number | null;
  temperatureC?: number | null;
  spo2Percent?: number | null;
  heightCm?: number | null;
  weightKg?: number | null;
  clinicalFindings?: string | null;
  clinicalConclusion?: string | null;
  amendmentReason?: string | null;
  isConfirmed?: boolean | null;
  expectedStateVersion?: number | null;
  expectedRevisionNo?: number | null;
}

export interface CreateDiagnosticOrderItemRequest {
  serviceId?: number | null;
  serviceCode?: string | null;
  instruction?: string | null;
  priority?: DiagnosticOrderPriority | (string & {}) | null;
  requiredForConclusion?: boolean | null;
}

export interface CreateDiagnosticOrderRequest {
  serviceId?: number | null;
  serviceCode?: string | null;
  instruction?: string | null;
  priority?: DiagnosticOrderPriority | (string & {}) | null;
  requiredForConclusion?: boolean | null;
  items?: CreateDiagnosticOrderItemRequest[] | null;
  expectedStateVersion?: number | null;
  transitionToAwaitingResults?: boolean | null;
  clinicalNote?: string | null;
}

export interface CompleteVisitRequest {
  clinicalConclusion?: string | null;
  dischargeInstructions?: string | null;
  followUpInstructions?: string | null;
  followUpDate?: string | null;
  warningSigns?: string | null;
  notes?: string | null;
  completionNote?: string | null;
  confirmNoPrescription?: boolean | null;
  expectedStateVersion?: number | null;
  expectedRevisionNo?: number | null;
}

export interface Icd10CatalogItemDto {
  id: number;
  code: string;
  nameVi: string;
  catalogVersion: string | null;
  nodeType: string | null;
  isSelectable: boolean;
  notPrimaryDx: boolean;
}

export interface SearchIcd10CodesParams {
  q?: string;
  primaryOnly?: boolean;
  limit?: number;
}

export interface RecordDiagnosisItemRequest {
  icd10CodeId?: number | null;
  code?: string | null;
  icd10Code?: string | null;
  name?: string | null;
  isPrimary?: boolean | null;
  clinicalDiagnosis?: string | null;
  status?: ConsultationDiagnosisStatus | null;
}

export interface RecordDiagnosisRequest {
  diagnoses?: RecordDiagnosisItemRequest[] | null;
  primaryIcd10CodeId?: number | null;
  primaryIcd10Code?: string | null;
  primaryClinicalDiagnosis?: string | null;
  secondaryIcd10CodeIds?: number[] | null;
  secondaryIcd10Codes?: string[] | null;
  clinicalConclusion?: string | null;
  clinicalFindings?: string | null;
  differentialNotes?: string | null;
  lifestyleInstructions?: string | null;
  warningSigns?: string | null;
  amendmentReason?: string | null;
  diagnosisStatus?: ConsultationDiagnosisStatus | null;
  isConfirmed?: boolean | null;
  expectedStateVersion?: number | null;
  expectedRevisionNo?: number | null;
}
