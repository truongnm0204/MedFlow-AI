import { apiClient } from '@/shared/lib/axios';

import type {
  AuditLogDetailDto,
  AuditLogFilterParams,
  AuditLogListResponseDto,
} from '../types/audit-log.types';

export async function fetchAuditLogs(
  params: AuditLogFilterParams = {},
): Promise<AuditLogListResponseDto> {
  const query: Record<string, string | number> = {};

  if (params.keyword?.trim()) query.keyword = params.keyword.trim();
  if (params.action?.trim()) query.action = params.action.trim();
  if (params.entityType?.trim()) query.entityType = params.entityType.trim();
  if (typeof params.actorUserId === 'number' && params.actorUserId > 0) {
    query.actorUserId = params.actorUserId;
  }
  if (typeof params.hospitalId === 'number' && params.hospitalId > 0) {
    query.hospitalId = params.hospitalId;
  }
  if (params.fromDate?.trim()) query.fromDate = params.fromDate.trim();
  if (params.toDate?.trim()) query.toDate = params.toDate.trim();
  if (typeof params.page === 'number') query.page = params.page;
  if (typeof params.pageSize === 'number') query.pageSize = params.pageSize;

  const { data } = await apiClient.get<AuditLogListResponseDto>('/admin/audit-logs', {
    params: query,
  });
  return data;
}

export async function fetchAuditLogDetail(id: number): Promise<AuditLogDetailDto> {
  const { data } = await apiClient.get<AuditLogDetailDto>(`/admin/audit-logs/${id}`);
  return data;
}
