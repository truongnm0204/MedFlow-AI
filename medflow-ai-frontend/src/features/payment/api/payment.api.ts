import { apiClient } from '@/shared/lib/axios';

import type { GenerateQrPaymentResponse, VisitChargesResponse } from '../types/payment.types';

/**
 * Hai endpoint tiền: không fallback mock. Sai một lần là hiển thị sai số tiền.
 */
export async function fetchVisitChargesApi(visitCode: string): Promise<VisitChargesResponse> {
  const { data } = await apiClient.get<VisitChargesResponse>(
    `/patients/visits/${encodeURIComponent(visitCode)}/charges`,
  );
  return data;
}

export async function generateQrPaymentApi(visitCode: string): Promise<GenerateQrPaymentResponse> {
  const { data } = await apiClient.post<GenerateQrPaymentResponse>(
    `/patients/visits/${encodeURIComponent(visitCode)}/payments/qr`,
    {},
  );
  return data;
}
