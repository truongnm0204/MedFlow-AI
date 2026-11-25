import { useQuery } from '@tanstack/react-query';

import { fetchAuditLogDetail, fetchAuditLogs } from '../api/audit-log.api';
import type { AuditLogFilterParams } from '../types/audit-log.types';

export const ADMIN_AUDIT_LOGS_QUERY_KEY = 'admin-audit-logs';

export function useAuditLogs(params: AuditLogFilterParams, enabled = true) {
  return useQuery({
    queryKey: [ADMIN_AUDIT_LOGS_QUERY_KEY, 'list', params],
    queryFn: () => fetchAuditLogs(params),
    enabled,
  });
}

export function useAuditLogDetail(id: number | null) {
  return useQuery({
    queryKey: [ADMIN_AUDIT_LOGS_QUERY_KEY, 'detail', id],
    queryFn: () => fetchAuditLogDetail(id!),
    enabled: typeof id === 'number' && id > 0,
  });
}
