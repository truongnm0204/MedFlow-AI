export interface MedicineBatchOption {
  batchId?: number;
  batchCode: string;
  expiryDate: string;
  availableStock: number;
  shelfLocation: string;
  isFefoRecommended?: boolean;
}

export interface PrepareMedicineItem {
  id: string;
  prescriptionItemId?: number;
  orderIndex: number;
  drugName: string;
  dosageForm: string;
  usageInstruction: string;
  prescribedQty: number;
  pickedQty: number;
  unit: string;
  availableBatches: MedicineBatchOption[];
  selectedBatchId?: number | null;
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
  prescriptionId?: number;
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
  status?: string;
  items: PrepareMedicineItem[];
  gppChecks: GppCheckItem[];
}

export interface PrepareMedicineItemPayload {
  prescriptionItemId: number;
  medicineBatchId?: number | null;
  batchCode?: string | null;
  pickedQuantity: number;
}

export interface PreparePrescriptionPayload {
  items: PrepareMedicineItemPayload[];
  notes?: string | null;
}

export interface PreparePrescriptionResponse {
  prescriptionId: number;
  prescriptionCode: string;
  dispenseId: number;
  status: string;
  preparedAt: string;
  preparedByStaffName: string;
  preparedItemCount: number;
  message: string;
}
