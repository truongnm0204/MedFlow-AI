export interface PatientDiagnosticResultDetail {
  orderId: number;
  orderCode: string;
  visitId: number | null;
  visitCode: string;
  patientId: number;
  patientCode: string;
  patientName: string;
  gender: string;
  birthYear: number | null;
  relationshipLabel: string;
  hospitalName: string;
  serviceName: string;
  orderingDoctor: string;
  performingDoctor: string;
  performedRoom: string;
  signedAt: string;
  isPublishedToPatient: boolean;
  status: string;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  fileName: string;
}

export interface PatientDiagnosticResultListItem {
  orderId: number;
  orderCode: string;
  visitId: number | null;
  visitCode: string;
  patientId: number;
  patientCode: string;
  patientName: string;
  relationshipLabel: string;
  hospitalName: string;
  serviceName: string;
  orderingDoctorName: string;
  performedPlaceName: string;
  signedAt: string;
  isPublishedToPatient: boolean;
  status: string;
}

export interface PatientDiagnosticImageItem {
  id: string;
  label: string;
  description: string;
}

export interface PatientDiagnosticLabMetric {
  id: string;
  parameterName: string;
  value: string;
  unit: string;
  referenceRange: string;
  status: 'NORMAL' | 'HIGH' | 'LOW';
}

export interface PatientDiagnosticResultSheet {
  fileName: string;
  orderCode: string;
  visitCode: string;
  patientName: string;
  patientCode: string;
  gender: string;
  birthYear: number;
  serviceName: string;
  orderingDoctor: string;
  performingDoctor: string;
  performedRoom: string;
  signedAt: string;
  isPublishedToPatient: boolean;
  images: PatientDiagnosticImageItem[];
  endoscopicConclusion: string;
  endoscopicFindings: string[];
  labSectionTitle: string;
  labMetrics: PatientDiagnosticLabMetric[];
  physicianAdvice: string;
}
