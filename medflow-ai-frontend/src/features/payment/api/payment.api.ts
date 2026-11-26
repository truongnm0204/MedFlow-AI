import { apiClient } from '@/shared/lib/axios';

import type {
  DeclineDiagnosticOrderRequest,
  DeclineDiagnosticOrderResponse,
  GenerateQrPaymentRequest,
  GenerateQrPaymentResponse,
  PatientVisitListResponseDto,
  UpdateDiagnosticOrderSelectionRequest,
  VisitChargesResponse,
} from '../types/payment.types';

/**
 * Hai endpoint tiền: không fallback mock. Sai một lần là hiển thị sai số tiền.
 * GET /patients/visits/{idOrCode}/charges — nguồn trạng thái thanh toán để poll.
 */
export async function fetchVisitChargesApi(visitIdOrCode: string): Promise<VisitChargesResponse> {
  const { data } = await apiClient.get<VisitChargesResponse>(
    `/patients/visits/${encodeURIComponent(visitIdOrCode.trim())}/charges`,
  );
  return data;
}

export type QrPaymentCreateInput = number | null | undefined | GenerateQrPaymentRequest;

/** POST /patients/visits/{idOrCode}/payments/qr — 200 = REUSED/ADOPTED, 201 = CREATED. */
export async function generateQrPaymentApi(
  visitIdOrCode: string,
  input?: QrPaymentCreateInput,
): Promise<GenerateQrPaymentResponse> {
  let body: Record<string, unknown> = {};
  if (typeof input === 'number') {
    body = { visitChargeId: input };
  } else if (input && typeof input === 'object') {
    if (input.visitChargeIds && input.visitChargeIds.length > 0) {
      body.visitChargeIds = input.visitChargeIds;
    } else if (input.visitChargeId != null) {
      body.visitChargeId = input.visitChargeId;
    }
    if (input.idempotencyKey) {
      body.idempotencyKey = input.idempotencyKey;
    }
  }

  const { data } = await apiClient.post<GenerateQrPaymentResponse>(
    `/patients/visits/${encodeURIComponent(visitIdOrCode.trim())}/payments/qr`,
    body,
  );
  return data;
}

/** POST /patients/visits/{idOrCode}/diagnostic-orders/selection */
export async function updateDiagnosticOrderSelectionApi(
  visitIdOrCode: string,
  payload: UpdateDiagnosticOrderSelectionRequest,
): Promise<VisitChargesResponse> {
  const { data } = await apiClient.post<VisitChargesResponse>(
    `/patients/visits/${encodeURIComponent(visitIdOrCode.trim())}/diagnostic-orders/selection`,
    payload,
  );
  return data;
}

/** POST /patients/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline */
export async function declineDiagnosticOrderApi(
  visitIdOrCode: string,
  orderIdOrCode: number | string,
  payload?: DeclineDiagnosticOrderRequest,
): Promise<DeclineDiagnosticOrderResponse> {
  const { data } = await apiClient.post<DeclineDiagnosticOrderResponse>(
    `/patients/visits/${encodeURIComponent(visitIdOrCode.trim())}/diagnostic-orders/${encodeURIComponent(String(orderIdOrCode))}/decline`,
    payload ?? {},
  );
  return data;
}

/** GET /patients/me/visits — lượt khám thuộc quyền xem, tối đa 20. */
export async function fetchMyVisitsApi(limit = 20): Promise<PatientVisitListResponseDto> {
  const { data } = await apiClient.get<PatientVisitListResponseDto>('/patients/me/visits', {
    params: { limit },
  });
  return data;
}
