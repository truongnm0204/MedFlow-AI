export type AuditLogCategory = 'AUTH' | 'RBAC' | 'CONFIG' | 'OPERATIONS' | (string & {});

export interface AuditLogFilterParams {
  keyword?: string;
  action?: string;
  entityType?: string;
  actorUserId?: number | null;
  hospitalId?: number | null;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export interface AuditLogMetricsDto {
  totalLogs: number;
  last24HoursCount: number;
  authEventsCount: number;
  configAndRbacEventsCount: number;
}

export interface AuditLogHospitalOptionDto {
  id: number;
  code: string;
  name: string;
}

export interface AuditLogListItemDto {
  id: number;
  auditCode: string;
  actorUserId: number | null;
  actorEmail: string | null;
  actorFullName: string | null;
  hospitalId: number | null;
  hospitalName: string | null;
  action: string;
  category: AuditLogCategory;
  entityType: string;
  entityId: string | null;
  reason: string | null;
  ipAddress: string | null;
  occurredAt: string;
  occurredAtHospitalLocal: string;
  hasMetadata: boolean;
  changeData: string | null;
}

export interface AuditLogDetailDto {
  id: number;
  auditCode: string;
  actorUserId: number | null;
  actorEmail: string | null;
  actorFullName: string | null;
  hospitalId: number | null;
  hospitalName: string | null;
  action: string;
  category: AuditLogCategory;
  entityType: string;
  entityId: string | null;
  reason: string | null;
  ipAddress: string | null;
  occurredAt: string;
  occurredAtHospitalLocal: string;
  changeData: string | null;
  redactedFields: string[];
}

export interface AuditLogListResponseDto {
  data: AuditLogListItemDto[];
  items: AuditLogListItemDto[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  metrics: AuditLogMetricsDto;
  availableActions: string[];
  availableEntityTypes: string[];
  availableHospitals: AuditLogHospitalOptionDto[];
}
