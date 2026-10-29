import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchDoctorsApi } from '../api/doctor-search.api';
import type { DoctorFilterParams, DoctorSearchResponse } from '../types/doctor-search.types';

export function useDoctorSearch(
  params: DoctorFilterParams,
): UseQueryResult<DoctorSearchResponse, Error> {
  return useQuery<DoctorSearchResponse, Error>({
    queryKey: ['doctor-search', params],
    queryFn: () => fetchDoctorsApi(params),
    placeholderData: (prev) => prev,
  });
}
