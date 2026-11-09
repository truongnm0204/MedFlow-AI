export type MedicalHistoryFilterStatus =
  | 'ALL'
  | 'COMPLETED'
  | 'IN_PROGRESS'
  | 'IN_CONSULTATION'
  | 'WAITING'
  | 'CALLED'
  | 'AWAITING_RESULTS'
  | 'FINALIZED'
  | 'DRAFT'
  | 'CHECKED_IN';

export type MedicalHistoryLookupMode = 'visit' | 'patient';

export interface GetPatientMedicalHistoryQueryParams {
  visitIdOrCode?: string;
  status?: string;
  specialtyCode?: string;
  departmentId?: number;
  fromDate?: string;
  toDate?: string;
  keyword?: string;
  includeCurrentVisit?: boolean;
  limit?: number;
}

export interface MedicalHistoryPatientSummaryDto {
  patientId: number;
  patientCode: string;
  fullName: string;
  gender: string;
  dateOfBirth: string | null;
  age: number;
  bloodType: string | null;
  allergiesSummary: string;
  chronicConditions: string[];
}

export interface MedicalHistoryCurrentVisitDto {
  visitId: number;
  visitCode: string;
  status: string;
  reasonForVisit: string | null;
  checkedInAt: string | null;
  roomCode: string | null;
  roomName: string | null;
  departmentName: string | null;
  doctorName: string | null;
}

export interface MedicalHistoryStatsDto {
  totalVisits: number;
  completedVisits: number;
  finalizedRecordsCount: number;
  totalDiagnosesCount: number;
  totalDiagnosticOrdersCount: number;
  totalPrescriptionsCount: number;
  lastVisitAt: string | null;
}

export interface MedicalHistoryVitalSignsDto {
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

export interface MedicalHistoryRevisionSummaryDto {
  revisionId: number;
  revisionNo: number;
  recordedAt: string | null;
  editedByDoctorName: string | null;
  isConfirmed: boolean;
  amendmentReason: string | null;
  clinicalConclusion: string | null;
}

export interface MedicalHistoryAddendumDto {
  addendumId: number;
  recordType: string;
  targetRecordId: number | null;
  reason: string | null;
  Note?: string | null;
  note: string | null;
  correctedContentJson: string | null;
  authorDoctorName: string | null;
  recordedAt: string | null;
}

export interface MedicalHistoryRecordDto {
  medicalRecordId: number;
  status: string;
  finalizedAt: string | null;
  latestRevisionNo: number;
  isConfirmed: boolean;
  recordedAt: string | null;
  editedByDoctorName: string | null;
  presentingComplaint: string | null;
  symptomHistory: string | null;
  relevantHistoryAllergies: string | null;
  clinicalFindings: string | null;
  clinicalConclusion: string | null;
  amendmentReason: string | null;
  vitalSigns: MedicalHistoryVitalSignsDto | null;
  revisionCount: number;
  revisions: MedicalHistoryRevisionSummaryDto[];
  addenda: MedicalHistoryAddendumDto[];
}

export interface MedicalHistoryDiagnosisDto {
  diagnosisId: number;
  icd10CodeId: number | null;
  icd10Code: string;
  icd10NameVi: string;
  catalogVersion: string | null;
  clinicalDiagnosis: string | null;
  isPrimary: boolean;
  status: string;
  confirmedAt: string | null;
  recordedAt: string | null;
  recordedByDoctorName: string | null;
}

export interface MedicalHistoryDiagnosticOrderDto {
  orderId: number;
  orderCode: string;
  serviceId: number | null;
  serviceCode: string | null;
  serviceName: string;
  priority: string;
  status: string;
  requiredForConclusion: boolean;
  instruction: string | null;
  orderedAt: string | null;
  confirmedAt: string | null;
  resultStatus: string | null;
  resultFinalizedAt: string | null;
  resultVersionNo: number | null;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  isResultFinalized: boolean;
}

export interface MedicalHistoryPrescriptionItemDto {
  itemId: number;
  medicineId: number | null;
  medicineCode: string | null;
  medicineName: string;
  activeIngredient: string | null;
  strength: string | null;
  dosageForm: string | null;
  dispensingUnit: string | null;
  prescribedQuantity: number | null;
  singleDoseQuantity: number | null;
  singleDoseUnit: string | null;
  doseScheduleText: string | null;
  frequency: string | null;
  durationDays: number | null;
  administrationRoute: string | null;
  instructions: string | null;
}

export interface MedicalHistoryPrescriptionDto {
  prescriptionId: number;
  prescriptionCode: string;
  status: string;
  fulfillmentChoice: string | null;
  issuedAt: string | null;
  prescribingDoctorName: string | null;
  items: MedicalHistoryPrescriptionItemDto[];
}

export interface MedicalHistoryVisitEntryDto {
  visitId: number;
  visitCode: string;
  visitDate: string;
  checkedInAt: string | null;
  completedAt: string | null;
  status: string;
  visitType: string;
  reasonForVisit: string | null;
  departmentId: number | null;
  departmentName: string;
  specialtyCode: string | null;
  specialtyName: string;
  roomCode: string | null;
  roomName: string | null;
  doctorId: number | null;
  doctorCode: string | null;
  doctorName: string;
  isAssignedToCurrentDoctor: boolean;
  isCurrentVisit: boolean;
  medicalRecord: MedicalHistoryRecordDto | null;
  diagnoses: MedicalHistoryDiagnosisDto[];
  diagnosticOrders: MedicalHistoryDiagnosticOrderDto[];
  prescriptions: MedicalHistoryPrescriptionDto[];
}

export interface PatientMedicalHistoryResponseDto {
  facilityName: string;
  facilityCode: string;
  generatedAtUtc: string;
  patient: MedicalHistoryPatientSummaryDto;
  currentVisit: MedicalHistoryCurrentVisitDto | null;
  stats: MedicalHistoryStatsDto;
  visits: MedicalHistoryVisitEntryDto[];
}
