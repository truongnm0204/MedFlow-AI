export interface ReceiverVerificationInfo {
  fullName: string;
  patientCode: string;
  identityCard: string;
  phone: string;
  receiverName: string;
  relationship: string;
  isIdentityVerified: boolean;
}

export interface DispenseMedicationCounselingItem {
  id: string;
  orderIndex: number;
  prescriptionItemId?: number;
  medicineId?: number;
  drugName: string;
  qtyLabel: string;
  prescribedQuantity?: number;
  dispensedQuantity?: number;
  unit?: string;
  batchCode: string;
  batchId?: number | null;
  expiryDate: string;
  dosageInstruction: string;
  warningNote: string;
  tagLabel?: string;
  tagTone?: 'sky' | 'rose' | 'amber';
  shelfLocation?: string | null;
  unitPrice?: number | null;
  unitPriceFormatted?: string | null;
  lineTotal?: number | null;
  lineTotalFormatted?: string | null;
}

export interface PharmacistHandoverCheck {
  id: string;
  label: string;
  checked: boolean;
}

export interface DispenseConfirmContext {
  prescriptionId?: number;
  rxCode: string;
  ticketNo: string;
  visitCode: string;
  issuedAt: string;
  doctorName: string;
  totalPaidFormatted: string;
  paymentMethodLabel: string;
  paymentStatus?: string;
  regimenBadge: string;
  receiver: ReceiverVerificationInfo;
  medications: DispenseMedicationCounselingItem[];
  checks: PharmacistHandoverCheck[];
  canDispense?: boolean;
  disabledReason?: string | null;
  dispenseStatus?: string;
}

export interface ConfirmDispenseItemPayload {
  prescriptionItemId: number;
  medicineBatchId?: number | null;
  dispensedQuantity?: number | null;
  shortageReason?: string | null;
}

export interface ConfirmDispensePayload {
  prescriptionIdOrCode?: string;
  receiverName?: string;
  relationship?: string;
  receiverIdentityCard?: string;
  receiverPhone?: string;
  isIdentityVerified: boolean;
  checksConfirmed: boolean;
  items?: ConfirmDispenseItemPayload[];
  idempotencyKey?: string;
  notes?: string;
}
