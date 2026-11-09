import type {
  DoctorQueueArrivalClass,
  DoctorQueueGroup,
  DoctorQueueIneligibleReason,
  DoctorQueuePresenceStatus,
} from './doctor-assigned-queue.types';
import type { AssignedQueueType, VisitPriority } from './doctor.types';

export type ConsultationActionOutcome =
  'VIEWED' | 'OPENED' | 'ALREADY_IN_PROGRESS' | 'CLINICAL_EXAMINATION_RECORDED';

export type ConsultationWorkspaceState =
  'WAITING' | 'CALLED' | 'IN_PROGRESS' | 'AWAITING_RESULTS' | 'RETURN_CONSULTATION' | 'COMPLETED';

export type ConsultationMedicalRecordStatus = 'NOT_CREATED' | 'DRAFT' | 'FINALIZED';

export type ConsultationDiagnosisStatus = 'PRELIMINARY' | 'CONFIRMED';

export type ConsultationWorkspaceActionId =
  | 'open-consultation'
  | 'view-medical-history'
  | 'record-clinical-examination'
  | 'create-diagnostic-order'
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
