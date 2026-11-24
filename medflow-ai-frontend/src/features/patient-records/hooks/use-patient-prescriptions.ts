import { useQuery } from '@tanstack/react-query';

import { getMyPrescriptionDetail, getMyPrescriptions } from '../api/patient-records.api';

export function usePatientPrescriptions(params: { page?: number; pageSize?: number } = {}) {
  const page = params.page ?? 1;
  const pageSize = params.pageSize ?? 20;
  return useQuery({
    queryKey: ['patient-prescriptions', { page, pageSize }],
    queryFn: () => getMyPrescriptions({ page, pageSize }),
    retry: false,
  });
}

export function usePatientPrescriptionDetail(idOrCode: string | undefined) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['patient-prescription-detail', normalized],
    queryFn: () => getMyPrescriptionDetail(normalized),
    enabled: normalized.length > 0,
    retry: false,
  });
}
