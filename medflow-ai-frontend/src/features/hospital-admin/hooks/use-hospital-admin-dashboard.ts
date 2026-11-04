import { useQuery } from '@tanstack/react-query';

import { fetchHospitalAdminDashboardSummary } from '../api/hospital-admin.api';

export function useHospitalAdminDashboard(enabled = true) {
  return useQuery({
    queryKey: ['hospital-admin', 'dashboard'],
    queryFn: fetchHospitalAdminDashboardSummary,
    enabled,
    staleTime: 60_000,
  });
}
