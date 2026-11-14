export type ChargeSource = 'PERSISTED' | 'DERIVED';

export type ChargeDataStatus = 'VALID' | 'INVALID' | 'UNPRICED';

export type SettlementStatus = 'UNPAID' | 'PAID' | 'MISMATCH' | 'UNDETERMINED';

export type PaymentStatus = 'UNPAID' | 'PENDING' | 'PAID' | 'EXPIRED' | 'CANCELLED' | 'FAILED';

export type QrPaymentIssueKind = 'CREATED' | 'ADOPTED' | 'REUSED';

export interface VisitChargeLine {
  chargeId: number | null;
  chargeSource: ChargeSource;
  serviceId: number | null;
  serviceCode: string | null;
  serviceName: string | null;
  diagnosticOrderId: number | null;
  unitPrice: number | null;
  paymentRequirement: string | null;
  payable: boolean | null;
  chargeDataStatus: ChargeDataStatus;
}

export interface VisitChargesResponse {
  visitId: number;
  visitCode: string | null;
  visitStatus: string | null;
  patientId: number | null;
  charges: VisitChargeLine[];
  payableTotal: number;
  hasUnpricedPayableCharges: boolean;
  amountDue: number | null;
  currency: string;
  recognizedPaidTotal: number;
  settlementStatus: SettlementStatus;
  paymentId: number | null;
  paymentCode: string | null;
  paymentStatus: PaymentStatus | null;
  paymentAmount: number | null;
  paymentMethod: string | null;
  paidAt: string | null;
  externalReference: string | null;
  canPayByQr: boolean;
}

export interface GenerateQrPaymentResponse {
  visitId: number;
  visitCode: string | null;
  paymentId: number;
  paymentCode: string;
  amount: number;
  currency: string;
  status: PaymentStatus;
  qrPayload: string;
  bankBin: string;
  accountNumber: string;
  accountName: string;
  transferContent: string;
  issueKind: QrPaymentIssueKind;
}
