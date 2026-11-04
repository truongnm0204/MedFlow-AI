import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { buildFallbackExaminationTicket } from '../data/reception-mock-data';
import type {
  ExaminationTicketDto,
  PrintExaminationTicketRequest,
} from '../types/visit-ticket.types';

/**
 * GET /api/v1/reception/visits/{idOrCode}/ticket — Lấy thông tin phiếu khám bệnh ngoại trú (UC29).
 * Các lỗi phân quyền/nghiệp vụ từ Backend (400/401/403/404/409) được rethrow nguyên vẹn.
 */
export async function fetchExaminationTicket(idOrCode: string): Promise<ExaminationTicketDto> {
  const normalized = idOrCode.trim();
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<ExaminationTicketDto>(
        `/reception/visits/${encodeURIComponent(normalized)}/ticket`,
      );
      return data;
    } catch (error) {
      if (!(axios.isAxiosError(error) && !error.response)) {
        throw error;
      }
    }
  }

  return buildFallbackExaminationTicket(normalized);
}

/**
 * POST /api/v1/reception/visits/{idOrCode}/ticket/print — Thực hiện in phiếu khám bệnh và ghi nhật ký kiểm toán (UC29).
 */
export async function printExaminationTicket(
  idOrCode: string,
  payload?: PrintExaminationTicketRequest,
): Promise<ExaminationTicketDto> {
  const normalized = idOrCode.trim();
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.post<ExaminationTicketDto>(
        `/reception/visits/${encodeURIComponent(normalized)}/ticket/print`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!(axios.isAxiosError(error) && !error.response)) {
        throw error;
      }
    }
  }

  return buildFallbackExaminationTicket(normalized, payload, true);
}
