export type ChargeSource = 'PERSISTED' | 'DERIVED';

export type ChargeDataStatus = 'VALID' | 'INVALID' | 'UNPRICED';

export type ChargeType = 'EXAMINATION' | 'DIAGNOSTIC';

export type ChargeStatus = 'ACTIVE' | 'CANCELLED' | 'CANCELLED_REFUND_PENDING';

export type SettlementStatus = 'UNPAID' | 'PAID' | 'MISMATCH' | 'UNDETERMINED';

export type LineSettlementStatus = SettlementStatus | 'WAIVED' | 'CANCELLED';

export type PaymentStatus =
  'UNPAID' | 'PENDING' | 'PAID' | 'MISMATCH' | 'EXPIRED' | 'CANCELLED' | 'FAILED';

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
  chargeType?: ChargeType;
  chargeStatus?: ChargeStatus;
  lineSettlementStatus?: LineSettlementStatus;
  paymentId?: number | null;
  paymentCode?: string | null;
  paymentStatus?: PaymentStatus | null;
  canPayByQr?: boolean;
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
  visitChargeId?: number | null;
}

// ── Patient visit list (GET /patients/me/visits) ───────────────────────────────

export interface PatientVisitListItemDto {
  visitId: number;
  visitCode: string | null;
  visitStatus: string | null;
  checkedInAt: string | null;
  completedAt: string | null;
  hospitalName: string | null;
  paymentStatus: string | null;
  paymentCode: string | null;
}

export interface PatientVisitListResponseDto {
  items: PatientVisitListItemDto[];
}

// Aliases used by the my-visits / visit-payment pages (UC18).
export type QrPaymentResponseDto = GenerateQrPaymentResponse;
export type VisitChargesResponseDto = VisitChargesResponse;
export type VisitChargeLineDto = VisitChargeLine;
