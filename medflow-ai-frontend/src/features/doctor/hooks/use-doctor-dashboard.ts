import { useQuery } from '@tanstack/react-query';

import { fetchDoctorDashboardSummary } from '../api/doctor.api';
import type { DoctorDashboardQueryParams } from '../types/doctor.types';

export function useDoctorDashboard(params?: DoctorDashboardQueryParams, enabled = true) {
  return useQuery({
    queryKey: ['doctor', 'dashboard', params ?? null],
    queryFn: () => fetchDoctorDashboardSummary(params),
    enabled,
    staleTime: 60_000,
  });
}
