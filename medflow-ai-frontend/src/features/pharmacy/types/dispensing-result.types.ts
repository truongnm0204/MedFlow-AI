export interface DispensedLineItem {
  id: string;
  orderIndex: number;
  drugName: string;
  dosageForm: string;
  qtyDispensed: number;
  unit: string;
  batchCode: string;
  expiryDate: string;
  shelfLocation: string;
  unitPriceFormatted: string;
  lineTotalFormatted: string;
}

export interface DispensingResultDetail {
  dispenseCode: string;
  rxCode: string;
  visitCode: string;
  ticketNo: string;
  patientName: string;
  patientCode: string;
  receiverName: string;
  receiverIdentityMasked: string;
  prescribingDoctor: string;
  dispensingPharmacist: string;
  dispensedAt: string;
  statusLabel: string;
  paymentSummary: string;
  totalAmountFormatted: string;
  items: DispensedLineItem[];
  auditNote: string;
}
