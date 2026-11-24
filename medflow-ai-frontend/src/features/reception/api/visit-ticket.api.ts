import { apiClient } from '@/shared/lib/axios';

import type {
  ExaminationTicketDto,
  PrintExaminationTicketRequest,
} from '../types/visit-ticket.types';

/**
 * GET /api/v1/reception/visits/{idOrCode}/ticket — Lấy thông tin phiếu khám bệnh ngoại trú (UC29).
 */
export async function fetchExaminationTicket(idOrCode: string): Promise<ExaminationTicketDto> {
  const normalized = idOrCode.trim();
  const { data } = await apiClient.get<ExaminationTicketDto>(
    `/reception/visits/${encodeURIComponent(normalized)}/ticket`,
  );
  return data;
}

/**
 * POST /api/v1/reception/visits/{idOrCode}/ticket/print — Thực hiện in phiếu khám bệnh và ghi nhật ký kiểm toán (UC29).
 */
export async function printExaminationTicket(
  idOrCode: string,
  payload?: PrintExaminationTicketRequest,
): Promise<ExaminationTicketDto> {
  const normalized = idOrCode.trim();
  const { data } = await apiClient.post<ExaminationTicketDto>(
    `/reception/visits/${encodeURIComponent(normalized)}/ticket/print`,
    payload ?? {},
  );
  return data;
}
