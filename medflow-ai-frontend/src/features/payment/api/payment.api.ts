import { apiClient } from '@/shared/lib/axios';

import type {
  GenerateQrPaymentResponse,
  PatientVisitListResponseDto,
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

/** POST /patients/visits/{idOrCode}/payments/qr — 200 = REUSED/ADOPTED, 201 = CREATED. */
export async function generateQrPaymentApi(
  visitIdOrCode: string,
  visitChargeId?: number | null,
): Promise<GenerateQrPaymentResponse> {
  const body = visitChargeId != null ? { visitChargeId } : {};
  const { data } = await apiClient.post<GenerateQrPaymentResponse>(
    `/patients/visits/${encodeURIComponent(visitIdOrCode.trim())}/payments/qr`,
    body,
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
