import { useQuery } from '@tanstack/react-query';

import { getMyMedicalRecordDetail, getMyMedicalRecords } from '../api/patient-records.api';
import type { PatientMedicalRecordFilterParams } from '../types/patient-records.types';

export function usePatientMedicalRecords(params: PatientMedicalRecordFilterParams) {
  return useQuery({
    queryKey: ['patient-medical-records', params],
    queryFn: () => getMyMedicalRecords(params),
    retry: false,
  });
}

export function usePatientMedicalRecordDetail(idOrCode: string | undefined) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['patient-medical-record-detail', normalized],
    queryFn: () => getMyMedicalRecordDetail(normalized),
    enabled: normalized.length > 0,
    retry: false,
  });
}
