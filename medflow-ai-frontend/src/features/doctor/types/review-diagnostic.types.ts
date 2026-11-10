export type DiagnosticLabFlag = 'NORMAL' | 'HIGH' | 'LOW';

export interface DiagnosticLabIndicator {
  code: string;
  name: string;
  value: string;
  unit: string;
  referenceRange: string;
  flag: DiagnosticLabFlag;
}

export interface DiagnosticImageFrame {
  id: string;
  caption: string;
}

export interface DiagnosticResultAlertTag {
  label: string;
  tone: 'rose' | 'amber';
}

export interface DiagnosticResultItem {
  id: string;
  orderIndex: number;
  serviceCode: string;
  serviceName: string;
  performedBy: string;
  signedAtLabel: string;
  conclusionSummary: string;
  alertTag?: DiagnosticResultAlertTag;
  labIndicators: DiagnosticLabIndicator[];
  imageFrames: DiagnosticImageFrame[];
  anatomicalDescription: string[];
  recommendation: string;
  pdfFileName: string;
}

export interface ReviewDiagnosticPatient {
  fullName: string;
  patientCode: string;
  visitCode: string;
  allergies: string[];
}

export interface ReviewDiagnosticData {
  patient: ReviewDiagnosticPatient;
  results: DiagnosticResultItem[];
}
