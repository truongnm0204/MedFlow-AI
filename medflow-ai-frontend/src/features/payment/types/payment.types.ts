export type ChargeSource = 'PERSISTED' | 'DERIVED';

export type ChargeDataStatus = 'VALID' | 'INVALID' | 'UNPRICED';

export type ChargeType = 'EXAMINATION' | 'DIAGNOSTIC';

export type ChargeStatus =
  'ACTIVE' | 'CANCELLED' | 'PAID_NOT_PERFORMED' | 'CANCELLED_REFUND_PENDING';

export type SettlementStatus = 'UNPAID' | 'PAID' | 'MISMATCH' | 'UNDETERMINED';

export type LineSettlementStatus = SettlementStatus | 'WAIVED' | 'CANCELLED' | 'PAID_NOT_PERFORMED';

export type DiagnosticSelectionStatus =
  'UNDECIDED' | 'ACCEPTED' | 'DECLINED' | 'REVIEW_REQUIRED' | 'SKIPPED_AFTER_PAID';

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
  diagnosticOrderCode?: string | null;
  requiredForConclusion?: boolean | null;
  selectionStatus?: DiagnosticSelectionStatus | null;
  canSelect?: boolean;
  canDecline?: boolean;
  canSkipPaid?: boolean;
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
  selectedAmountDue?: number | null;
  activePendingQrPaymentId?: number | null;
  activePendingQrPaymentCode?: string | null;
  activePendingQrAmount?: number | null;
  activePendingQrChargeIds?: number[] | null;
  hasReconciliationItems?: boolean;
  readyForReturnAt?: string | null;
  isPresentToday?: boolean;
  presenceState?: string | null;
}

export interface PaymentAllocationItemDto {
  allocationId?: number | null;
  visitChargeId: number;
  chargeType?: string;
  diagnosticOrderId?: number | null;
  serviceCode?: string | null;
  serviceName: string | null;
  allocatedAmount: number;
  status: string;
}

export interface GenerateQrPaymentRequest {
  visitChargeId?: number | null;
  visitChargeIds?: number[] | null;
  idempotencyKey?: string | null;
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
  allocations?: PaymentAllocationItemDto[] | null;
  expiresAtLocal?: string | null;
}

export interface UpdateDiagnosticOrderSelectionRequest {
  acceptedOrderIds?: number[] | null;
  undecidedOrderIds?: number[] | null;
  reviewRequestedOrderIds?: number[] | null;
  reason?: string | null;
}

export interface DeclineDiagnosticOrderRequest {
  reason?: string | null;
  confirmAbandonPaid?: boolean | null;
}

export interface DeclineDiagnosticOrderResponse {
  visitId: number;
  visitCode: string | null;
  visitStatus: string | null;
  orderId: number;
  orderCode: string | null;
  orderStatus: string;
  selectionStatus: DiagnosticSelectionStatus;
  visitChargeId: number | null;
  chargeStatus: ChargeStatus | null;
  wasAlreadyPaid: boolean;
  cancelledPendingQrCount: number;
  transitionedToReturnConsultation: boolean;
  chargesSnapshot: VisitChargesResponse;
}

export interface CollectCashPaymentRequest {
  visitChargeIds: number[];
  idempotencyKey: string;
  notes?: string | null;
}

export interface CollectCashPaymentResponse {
  visitId: number;
  visitCode: string | null;
  paymentId: number;
  paymentCode: string;
  amount: number;
  currency: string;
  status: string;
  paymentMethod: string;
  collectedByStaffId: number;
  collectedByStaffName: string | null;
  idempotencyKey: string;
  paidAt: string;
  isIdempotentReplay: boolean;
  allocations: PaymentAllocationItemDto[];
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
  readyForReturnAt?: string | null;
  isPresentToday?: boolean;
  presenceState?: string | null;
}

export interface PatientVisitListResponseDto {
  items: PatientVisitListItemDto[];
}

// Aliases used by the my-visits / visit-payment pages (UC18).
export type QrPaymentResponseDto = GenerateQrPaymentResponse;
export type VisitChargesResponseDto = VisitChargesResponse;
export type VisitChargeLineDto = VisitChargeLine;
