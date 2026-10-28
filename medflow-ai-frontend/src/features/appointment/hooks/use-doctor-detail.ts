import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchDoctorDetailApi } from '../api/doctor-detail.api';
import type { DoctorDetailProfile } from '../types/doctor-detail.types';

export function useDoctorDetail(
  doctorId?: string | null,
): UseQueryResult<DoctorDetailProfile, Error> {
  return useQuery<DoctorDetailProfile, Error>({
    queryKey: ['doctor-detail', doctorId],
    queryFn: () => fetchDoctorDetailApi(doctorId!),
    enabled: Boolean(doctorId),
  });
}

