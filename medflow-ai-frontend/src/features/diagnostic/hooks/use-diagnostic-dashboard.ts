import { useQuery } from '@tanstack/react-query';

import { fetchDiagnosticDashboardSummary } from '../api/diagnostic.api';

export function useDiagnosticDashboard(enabled = true) {
  return useQuery({
    queryKey: ['diagnostic', 'dashboard'],
    queryFn: () => fetchDiagnosticDashboardSummary(),
    enabled,
    staleTime: 60_000,
  });
}
