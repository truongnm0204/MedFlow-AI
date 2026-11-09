export interface PatientDiagnosticImageFrame {
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
  images: PatientDiagnosticImageFrame[];
  endoscopicConclusion: string;
  endoscopicFindings: string[];
  labSectionTitle: string;
  labMetrics: PatientDiagnosticLabMetric[];
  physicianAdvice: string;
}
