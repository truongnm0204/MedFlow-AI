import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { buildMockMedicalHistoryResponse } from '../data/medical-history-mock';
import type {
  GetPatientMedicalHistoryQueryParams,
  PatientMedicalHistoryResponseDto,
} from '../types/medical-history.types';

/** Chỉ fallback về mock khi offline/network error (endpoint thực tế đã có và trả 4xx/5xx cho lỗi nghiệp vụ). */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response;
}

export function sanitizeHistoryQueryParams(
  params?: GetPatientMedicalHistoryQueryParams,
): Record<string, string | number | boolean> | undefined {
  if (!params) return undefined;

  const clean: Record<string, string | number | boolean> = {};

  const visitIdOrCode = params.visitIdOrCode?.trim();
  if (visitIdOrCode) {
    clean.visitIdOrCode = visitIdOrCode;
  }

  const status = params.status?.trim();
  if (status && status.toUpperCase() !== 'ALL') {
    clean.status = status;
  }

  const specialtyCode = params.specialtyCode?.trim();
  if (specialtyCode && specialtyCode.toUpperCase() !== 'ALL') {
    clean.specialtyCode = specialtyCode;
  }

  if (typeof params.departmentId === 'number' && params.departmentId > 0) {
    clean.departmentId = params.departmentId;
  }

  const fromDate = params.fromDate?.trim();
  if (fromDate) {
    clean.fromDate = fromDate;
  }

  const toDate = params.toDate?.trim();
  if (toDate) {
    clean.toDate = toDate;
  }

  const keyword = params.keyword?.trim();
  if (keyword) {
    clean.keyword = keyword;
  }

  if (typeof params.includeCurrentVisit === 'boolean') {
    clean.includeCurrentVisit = params.includeCurrentVisit;
  }

  if (typeof params.limit === 'number' && params.limit > 0) {
    clean.limit = params.limit;
  }

  return Object.keys(clean).length > 0 ? clean : undefined;
}

/**
 * Lấy lịch sử khám bệnh lâm sàng theo bệnh nhân (`GET /doctor/patients/{patientIdOrCode}/medical-history`).
 * Chỉ fallback về mock khi chưa có access token hoặc lỗi mạng không có HTTP response;
 * rethrow mọi HTTP error (400/401/403/404/409/500) để UI hiển thị cảnh báo ProblemDetails.
 */
export async function fetchPatientMedicalHistory(
  patientIdOrCode: string,
  params?: GetPatientMedicalHistoryQueryParams,
): Promise<PatientMedicalHistoryResponseDto> {
  const encoded = encodeURIComponent(patientIdOrCode.trim());
  const cleanParams = sanitizeHistoryQueryParams(params);
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<PatientMedicalHistoryResponseDto>(
        `/doctor/patients/${encoded}/medical-history`,
        cleanParams ? { params: cleanParams } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return buildMockMedicalHistoryResponse(patientIdOrCode, 'patient', params);
}

/**
 * Lấy lịch sử khám bệnh lâm sàng từ ngữ cảnh lượt khám (`GET /doctor/visits/{visitIdOrCode}/medical-history`).
 * Chỉ fallback về mock khi chưa có access token hoặc lỗi mạng không có HTTP response;
 * rethrow mọi HTTP error (400/401/403/404/409/500) để UI hiển thị cảnh báo ProblemDetails.
 */
export async function fetchVisitMedicalHistory(
  visitIdOrCode: string,
  params?: Omit<GetPatientMedicalHistoryQueryParams, 'visitIdOrCode'>,
): Promise<PatientMedicalHistoryResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const cleanParams = sanitizeHistoryQueryParams(params);
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<PatientMedicalHistoryResponseDto>(
        `/doctor/visits/${encoded}/medical-history`,
        cleanParams ? { params: cleanParams } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return buildMockMedicalHistoryResponse(visitIdOrCode, 'visit', params);
}
