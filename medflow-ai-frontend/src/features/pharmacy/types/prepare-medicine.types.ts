export interface MedicineBatchOption {
  batchCode: string;
  expiryDate: string;
  availableStock: number;
  shelfLocation: string;
  isFefoRecommended?: boolean;
}

export interface PrepareMedicineItem {
  id: string;
  orderIndex: number;
  drugName: string;
  dosageForm: string;
  usageInstruction: string;
  prescribedQty: number;
  pickedQty: number;
  unit: string;
  availableBatches: MedicineBatchOption[];
  selectedBatchCode: string;
  isPrepared: boolean;
}

export interface GppCheckItem {
  id: string;
  title: string;
  description: string;
  checked: boolean;
}

export interface PreparePrescriptionContext {
  rxCode: string;
  visitCode: string;
  patientName: string;
  patientCode: string;
  gender: string;
  ageLabel: string;
  doctorName: string;
  roomLabel: string;
  primaryDiagnosis: string;
  secondaryDiagnosis: string;
  criticalAllergy: string;
  paymentBadge: string;
  items: PrepareMedicineItem[];
  gppChecks: GppCheckItem[];
}
