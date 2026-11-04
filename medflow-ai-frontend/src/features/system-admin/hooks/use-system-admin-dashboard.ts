import { useQuery } from '@tanstack/react-query';

import { fetchSystemAdminDashboard } from '../api/system-admin.api';
import type { GetSystemAdminDashboardParams } from '../types/system-admin.types';

export function useSystemAdminDashboard(
  params: GetSystemAdminDashboardParams = { recentAuditLimit: 10, hospitalId: null },
  enabled = true,
) {
  const recentAuditLimit = params.recentAuditLimit ?? 10;
  const hospitalId = params.hospitalId ?? null;

  return useQuery({
    queryKey: ['system-admin', 'dashboard', recentAuditLimit, hospitalId],
    queryFn: () => fetchSystemAdminDashboard({ recentAuditLimit, hospitalId }),
    enabled,
    staleTime: 60_000,
  });
}
