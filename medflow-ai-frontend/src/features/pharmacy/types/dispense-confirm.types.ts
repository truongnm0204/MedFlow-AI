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
  drugName: string;
  qtyLabel: string;
  batchCode: string;
  expiryDate: string;
  dosageInstruction: string;
  warningNote: string;
  tagLabel?: string;
  tagTone?: 'sky' | 'rose' | 'amber';
}

export interface PharmacistHandoverCheck {
  id: string;
  label: string;
  checked: boolean;
}

export interface DispenseConfirmContext {
  rxCode: string;
  ticketNo: string;
  visitCode: string;
  issuedAt: string;
  doctorName: string;
  totalPaidFormatted: string;
  paymentMethodLabel: string;
  regimenBadge: string;
  receiver: ReceiverVerificationInfo;
  medications: DispenseMedicationCounselingItem[];
  checks: PharmacistHandoverCheck[];
}
