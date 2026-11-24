export type BatchStatus = 'AVAILABLE' | 'FEFO_RECOMMENDED' | 'EXPIRED' | 'BLOCKED';

export interface FefoBatchRecord {
  id: string;
  batchCode: string;
  drugName: string;
  manufacturer: string;
  expiryDate: string;
  daysUntilExpiry: number;
  availableQty: number;
  unit: string;
  shelfLocation: string;
  status: BatchStatus;
  isSelectable?: boolean;
  isFefoRecommended?: boolean;
  blockReason?: string;
}

export interface MedicineBatchRequirement {
  id: string;
  drugName: string;
  dosageForm: string;
  requiredQty: number;
  unit: string;
  selectedBatchId: string | null;
  overrideReason?: string;
  batches: FefoBatchRecord[];
}

export function isRequirementFefoOverride(requirement: MedicineBatchRequirement): boolean {
  if (!requirement.selectedBatchId) return false;
  const chosen = requirement.batches.find((b) => b.id === requirement.selectedBatchId);
  const recommended = requirement.batches.find((b) => b.status === 'FEFO_RECOMMENDED');
  if (!chosen || !recommended || chosen.id === recommended.id) return false;
  return chosen.daysUntilExpiry > recommended.daysUntilExpiry || chosen.status === 'AVAILABLE';
}

export interface FefoBatchSelectionContext {
  prescriptionId: number;
  prescriptionCode: string;
  visitId: number;
  visitCode: string;
  patientCode: string;
  patientName: string;
  status: string;
  dispenseId?: number | null;
  todayHospitalDate: string;
  items: MedicineBatchRequirement[];
}

export interface SelectFefoBatchItemPayload {
  prescriptionItemId: number;
  medicineBatchId?: number | null;
  batchCode?: string | null;
  pickedQuantity?: number;
  overrideReason?: string | null;
}

export interface SelectFefoBatchesPayload {
  items: SelectFefoBatchItemPayload[];
  overrideReason?: string | null;
  notes?: string | null;
}

export interface SelectFefoBatchesResponse {
  prescriptionId: number;
  prescriptionCode: string;
  dispenseId: number;
  status: string;
  selectedAt: string;
  selectedByStaffName: string;
  itemCount: number;
  message: string;
}
