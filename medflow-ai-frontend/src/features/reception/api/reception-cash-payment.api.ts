import { apiClient } from '@/shared/lib/axios';
import type {
  CollectCashPaymentRequest,
  CollectCashPaymentResponse,
  DeclineDiagnosticOrderRequest,
  DeclineDiagnosticOrderResponse,
  VisitChargesResponse,
} from '@/features/payment/types/payment.types';

export async function fetchReceptionVisitChargesApi(
  visitIdOrCode: string,
): Promise<VisitChargesResponse> {
  const { data } = await apiClient.get<VisitChargesResponse>(
    `/reception/visits/${encodeURIComponent(visitIdOrCode.trim())}/charges`,
  );
  return data;
}

export async function collectReceptionCashPaymentApi(
  visitIdOrCode: string,
  payload: CollectCashPaymentRequest,
): Promise<CollectCashPaymentResponse> {
  const { data } = await apiClient.post<CollectCashPaymentResponse>(
    `/reception/visits/${encodeURIComponent(visitIdOrCode.trim())}/payments/cash`,
    payload,
  );
  return data;
}

export async function declineReceptionDiagnosticOrderApi(
  visitIdOrCode: string,
  orderIdOrCode: number | string,
  payload: DeclineDiagnosticOrderRequest,
): Promise<DeclineDiagnosticOrderResponse> {
  const { data } = await apiClient.post<DeclineDiagnosticOrderResponse>(
    `/reception/visits/${encodeURIComponent(visitIdOrCode.trim())}/diagnostic-orders/${encodeURIComponent(String(orderIdOrCode))}/decline`,
    payload,
  );
  return data;
}
