export type PrescriptionSearchStatus =
  'PENDING_APPROVAL' | 'PREPARING' | 'READY_FOR_DISPENSE' | 'COMPLETED';

export interface PrescriptionSearchItem {
  id: string;
  queueNo: string;
  rxCode: string;
  issuedTime: string;
  patientName: string;
  patientCode: string;
  gender: string;
  age: number;
  doctorName: string;
  roomLabel: string;
  facility: string;
  diagnosisText: string;
  allergyWarning?: string;
  itemCount: number;
  totalUnits: number;
  totalPriceFormatted: string;
  status: PrescriptionSearchStatus;
  isUrgent?: boolean;
}

export interface PrescriptionSearchMetrics {
  pendingApproval: number;
  preparing: number;
  readyForDispense: number;
  completedInShift: number;
}

export interface PrescriptionSearchFilters {
  query: string;
  status: 'ALL' | PrescriptionSearchStatus;
  doctor: string;
}
