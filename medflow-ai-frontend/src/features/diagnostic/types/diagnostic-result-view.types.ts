export interface DiagnosticResultImageItem {
  id: string;
  label: string;
  caption: string;
  location: string;
  isAbnormal: boolean;
}

export interface DiagnosticAnatomicalSection {
  id: string;
  organName: string;
  statusLabel: string;
  statusTone: 'emerald' | 'amber' | 'rose';
  findings: Array<{ label: string; detail: string }>;
}

export interface DiagnosticResultDetailView {
  orderId: string;
  orderCode: string;
  serviceName: string;
  patientCode: string;
  patientName: string;
  gender: string;
  ageLabel: string;
  visitCode: string;
  allergyWarning: string;
  orderingDoctor: string;
  performingDoctor: string;
  roomName: string;
  equipmentName: string;
  priceFormatted: string;
  performedAt: string;
  signedAt: string;
  deviceBadge: string;
  images: DiagnosticResultImageItem[];
  microbiologyTest: {
    name: string;
    resultLabel: string;
    isPositive: boolean;
    notes: string;
  };
  videoAttachment: {
    fileName: string;
    durationLabel: string;
  };
  anatomicalSections: DiagnosticAnatomicalSection[];
  procedureChecklist: Array<{ id: string; label: string; checked: boolean }>;
  clinicalConclusion: string;
  icd10Tags: Array<{ code: string; name: string }>;
  followUpAdvice: string;
}
