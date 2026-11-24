export interface PatientEmrScopeMember {
  patientId: number;
  patientCode: string | null;
  fullName: string;
  relationshipLabel: string;
  isSelf: boolean;
}

export interface PatientEmrFilterOption {
  id: number;
  code: string | null;
  name: string;
}

export interface PatientMedicalRecordFilterParams {
  patientId?: number;
  fromDate?: string;
  toDate?: string;
  hospitalId?: number;
  specialtyId?: number;
  specialtyCode?: string;
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export interface PatientMedicalRecordListItem {
  medicalRecordId: number;
  recordCode: string;
  visitId: number;
  visitCode: string | null;
  visitDate: string;
  checkedInAt: string | null;
  completedAt: string | null;
  finalizedAt: string | null;
  patientId: number;
  patientCode: string | null;
  patientName: string;
  relationshipLabel: string;
  isSelfProfile: boolean;
  hospitalId: number | null;
  hospitalName: string | null;
  departmentName: string | null;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string | null;
  doctorName: string | null;
  primaryIcd10Code: string | null;
  primaryDiagnosisName: string | null;
  clinicalConclusion: string | null;
  diagnosisCount: number;
  diagnosticOrderCount: number;
  prescriptionCount: number;
}

export interface PatientMedicalRecordListResponse {
  items: PatientMedicalRecordListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  members: PatientEmrScopeMember[];
  hospitals: PatientEmrFilterOption[];
  specialties: PatientEmrFilterOption[];
}

export interface PatientMedicalRecordVitalSigns {
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

export interface PatientMedicalRecordDiagnosis {
  diagnosisId: number;
  icd10Code: string;
  icd10NameVi: string;
  clinicalDiagnosis: string | null;
  isPrimary: boolean;
  confirmedAt: string | null;
}

export interface PatientMedicalRecordDiagnosticSummary {
  orderId: number;
  orderCode: string;
  serviceCode: string | null;
  serviceName: string;
  priority: string;
  status: string;
  orderedAt: string | null;
  isResultPublished: boolean;
  signedAt: string | null;
  conclusionSummary: string | null;
  hasReportFile: boolean;
}

export interface PatientMedicalRecordPrescriptionItemSummary {
  itemId: number;
  medicineCode: string | null;
  medicineName: string;
  strength: string | null;
  dosageForm: string | null;
  prescribedQuantity: number;
  dispensingUnit: string | null;
  doseScheduleText: string | null;
  frequency: string | null;
  durationDays: number | null;
  administrationRoute: string | null;
  instructions: string | null;
}

export interface PatientMedicalRecordPrescriptionSummary {
  prescriptionId: number;
  prescriptionCode: string | null;
  status: string;
  issuedAt: string | null;
  prescribingDoctorName: string | null;
  itemCount: number;
  canViewPrescriptionDetail: boolean;
  items: PatientMedicalRecordPrescriptionItemSummary[];
}

export interface PatientMedicalRecordDetail {
  medicalRecordId: number;
  recordCode: string;
  status: string;
  finalizedAt: string | null;
  visitId: number;
  visitCode: string | null;
  visitType: string;
  visitDate: string;
  checkedInAt: string | null;
  completedAt: string | null;
  reasonForVisit: string | null;
  patientId: number;
  patientCode: string | null;
  patientName: string;
  relationshipLabel: string;
  isSelfProfile: boolean;
  gender: string | null;
  dateOfBirth: string | null;
  bloodType: string | null;
  hospitalId: number | null;
  hospitalCode: string | null;
  hospitalName: string | null;
  departmentName: string | null;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string | null;
  roomCode: string | null;
  roomName: string | null;
  doctorName: string | null;
  presentingComplaint: string | null;
  symptomHistory: string | null;
  relevantHistoryAllergies: string | null;
  clinicalFindings: string | null;
  clinicalConclusion: string | null;
  recordedAt: string | null;
  vitalSigns: PatientMedicalRecordVitalSigns | null;
  diagnoses: PatientMedicalRecordDiagnosis[];
  diagnosticOrders: PatientMedicalRecordDiagnosticSummary[];
  prescriptions: PatientMedicalRecordPrescriptionSummary[];
}

export interface PatientPrescriptionListItem {
  id: number;
  prescriptionCode: string | null;
  visitId: number;
  visitCode: string | null;
  status: string;
  issuedAt: string | null;
  prescribingDoctorName: string | null;
  hospitalName: string | null;
  itemCount: number;
}

export interface PatientPrescriptionListResponse {
  items: PatientPrescriptionListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PatientPrescriptionMedicine {
  id: number;
  medicineCode: string | null;
  medicineName: string;
  activeIngredient: string | null;
  strength: string | null;
  dosageForm: string | null;
  prescribedQuantity: number;
  dispensingUnit: string | null;
  singleDoseQuantity: number | null;
  singleDoseUnit: string | null;
  doseScheduleText: string | null;
  frequency: string | null;
  durationDays: number | null;
  administrationRoute: string | null;
  instructions: string | null;
}

export interface PatientPrescriptionDetail {
  prescription: PatientPrescriptionListItem;
  items: PatientPrescriptionMedicine[];
}
