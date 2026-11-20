export type QrPaymentIssueKind = 'CREATED' | 'ADOPTED' | 'REUSED';

export interface QrPaymentResponseDto {
  visitId: number;
  visitCode: string | null;
  paymentId: number;
  paymentCode: string;
  amount: number;
  currency: string;
  status: string;
  qrPayload: string;
  bankBin: string;
  accountNumber: string;
  accountName: string;
  transferContent: string;
  issueKind: QrPaymentIssueKind;
}

export interface VisitChargeLineDto {
  chargeId: number | null;
  chargeSource: string;
  serviceId: number | null;
  serviceCode: string | null;
  serviceName: string | null;
  diagnosticOrderId: number | null;
  unitPrice: number | null;
  paymentRequirement: string | null;
  payable: boolean | null;
  chargeDataStatus: string;
}

export interface VisitChargesResponseDto {
  visitId: number;
  visitCode: string | null;
  visitStatus: string;
  patientId: number | null;
  charges: VisitChargeLineDto[];
  payableTotal: number;
  hasUnpricedPayableCharges: boolean;
  amountDue: number | null;
  currency: string;
  recognizedPaidTotal: number;
  settlementStatus: string;
  paymentId: number | null;
  paymentCode: string | null;
  paymentStatus: string | null;
  paymentAmount: number | null;
  paymentMethod: string | null;
  paidAt: string | null;
  externalReference: string | null;
  canPayByQr: boolean;
}

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
