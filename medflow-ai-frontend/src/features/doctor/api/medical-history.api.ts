import { apiClient } from '@/shared/lib/axios';

import type {
  GetPatientMedicalHistoryQueryParams,
  PatientMedicalHistoryResponseDto,
} from '../types/medical-history.types';

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
 */
export async function fetchPatientMedicalHistory(
  patientIdOrCode: string,
  params?: GetPatientMedicalHistoryQueryParams,
): Promise<PatientMedicalHistoryResponseDto> {
  const encoded = encodeURIComponent(patientIdOrCode.trim());
  const cleanParams = sanitizeHistoryQueryParams(params);
  const { data } = await apiClient.get<PatientMedicalHistoryResponseDto>(
    `/doctor/patients/${encoded}/medical-history`,
    cleanParams ? { params: cleanParams } : undefined,
  );
  return data;
}

/**
 * Lấy lịch sử khám bệnh lâm sàng từ ngữ cảnh lượt khám (`GET /doctor/visits/{visitIdOrCode}/medical-history`).
 */
export async function fetchVisitMedicalHistory(
  visitIdOrCode: string,
  params?: Omit<GetPatientMedicalHistoryQueryParams, 'visitIdOrCode'>,
): Promise<PatientMedicalHistoryResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const cleanParams = sanitizeHistoryQueryParams(params);
  const { data } = await apiClient.get<PatientMedicalHistoryResponseDto>(
    `/doctor/visits/${encoded}/medical-history`,
    cleanParams ? { params: cleanParams } : undefined,
  );
  return data;
}
