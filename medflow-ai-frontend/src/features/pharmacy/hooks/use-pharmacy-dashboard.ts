import { useQuery } from '@tanstack/react-query';

import { fetchPharmacyDashboardSummary } from '../api/pharmacy.api';

export function usePharmacyDashboard(enabled = true) {
  return useQuery({
    queryKey: ['pharmacy', 'dashboard'],
    queryFn: () => fetchPharmacyDashboardSummary(),
    enabled,
    staleTime: 60_000,
  });
}
