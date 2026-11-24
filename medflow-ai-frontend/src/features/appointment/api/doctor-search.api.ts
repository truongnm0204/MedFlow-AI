import { apiClient } from '@/shared/lib/axios';

import type { DoctorFilterParams, DoctorSearchResponse } from '../types/doctor-search.types';

export async function fetchDoctorsApi(
  params: DoctorFilterParams = {},
): Promise<DoctorSearchResponse> {
  const { data } = await apiClient.get<DoctorSearchResponse>('/doctors', {
    params: {
      keyword: params.keyword?.trim() || undefined,
      specialty: params.specialty ?? 'ALL',
      degree: params.degree ?? 'ALL',
      hasScheduleToday: params.hasScheduleToday ?? false,
      hasScheduleThisWeek: params.hasScheduleThisWeek ?? false,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 6,
    },
  });
  return data;
}
