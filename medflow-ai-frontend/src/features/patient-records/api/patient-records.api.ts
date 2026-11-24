import { apiClient } from '@/shared/lib/axios';

import type {
  PatientMedicalRecordDetail,
  PatientMedicalRecordFilterParams,
  PatientMedicalRecordListResponse,
  PatientPrescriptionDetail,
  PatientPrescriptionListResponse,
} from '../types/patient-records.types';

export async function getMyMedicalRecords(
  params: PatientMedicalRecordFilterParams = {},
): Promise<PatientMedicalRecordListResponse> {
  const cleanParams: Record<string, string | number> = {};
  if (params.patientId && params.patientId > 0) cleanParams.patientId = params.patientId;
  if (params.fromDate?.trim()) cleanParams.fromDate = params.fromDate.trim();
  if (params.toDate?.trim()) cleanParams.toDate = params.toDate.trim();
  if (params.hospitalId && params.hospitalId > 0) cleanParams.hospitalId = params.hospitalId;
  if (params.specialtyId && params.specialtyId > 0) cleanParams.specialtyId = params.specialtyId;
  if (params.specialtyCode?.trim()) cleanParams.specialtyCode = params.specialtyCode.trim();
  if (params.keyword?.trim()) cleanParams.keyword = params.keyword.trim();
  if (params.page && params.page > 0) cleanParams.page = params.page;
  if (params.pageSize && params.pageSize > 0) cleanParams.pageSize = params.pageSize;

  const { data } = await apiClient.get<PatientMedicalRecordListResponse>(
    '/patients/me/medical-records',
    { params: cleanParams },
  );
  return data;
}

export async function getMyMedicalRecordDetail(
  idOrCode: string,
): Promise<PatientMedicalRecordDetail> {
  const normalized = idOrCode.trim();
  const { data } = await apiClient.get<PatientMedicalRecordDetail>(
    `/patients/me/medical-records/${encodeURIComponent(normalized)}`,
  );
  return data;
}

export async function getMyPrescriptions(
  params: { page?: number; pageSize?: number } = {},
): Promise<PatientPrescriptionListResponse> {
  const { data } = await apiClient.get<PatientPrescriptionListResponse>(
    '/patients/me/prescriptions',
    {
      params: {
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 20,
      },
    },
  );
  return data;
}

export async function getMyPrescriptionDetail(
  idOrCode: string,
): Promise<PatientPrescriptionDetail> {
  const normalized = idOrCode.trim();
  const { data } = await apiClient.get<PatientPrescriptionDetail>(
    `/patients/me/prescriptions/${encodeURIComponent(normalized)}`,
  );
  return data;
}
