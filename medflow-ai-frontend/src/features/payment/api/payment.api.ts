import { apiClient } from '@/shared/lib/axios';

import type {
  PatientVisitListResponseDto,
  QrPaymentResponseDto,
  VisitChargesResponseDto,
} from '../types/payment.types';

function visitPath(visitIdOrCode: string): string {
  return `/visits/${encodeURIComponent(visitIdOrCode.trim())}`;
}

/** POST /visits/{idOrCode}/payments/qr — 200 = REUSED/ADOPTED, 201 = CREATED. */
export async function createQrPaymentApi(visitIdOrCode: string): Promise<QrPaymentResponseDto> {
  const { data } = await apiClient.post<QrPaymentResponseDto>(
    `${visitPath(visitIdOrCode)}/payments/qr`,
  );
  return data;
}

/** GET /visits/{idOrCode}/charges — nguồn trạng thái thanh toán để poll. */
export async function fetchVisitChargesApi(
  visitIdOrCode: string,
): Promise<VisitChargesResponseDto> {
  const { data } = await apiClient.get<VisitChargesResponseDto>(
    `${visitPath(visitIdOrCode)}/charges`,
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
