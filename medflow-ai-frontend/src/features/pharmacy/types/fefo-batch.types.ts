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
  blockReason?: string;
}

export interface MedicineBatchRequirement {
  id: string;
  drugName: string;
  dosageForm: string;
  requiredQty: number;
  unit: string;
  selectedBatchId: string | null;
  batches: FefoBatchRecord[];
}
