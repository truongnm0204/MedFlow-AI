export type SystemAdminPermissionCode =
  'USER_MANAGE' | 'HOSPITAL_CONFIG' | 'AUDIT_READ' | (string & {});

export type SystemAdminModuleCode =
  | 'STAFF_ACCOUNTS'
  | 'ROLES_PERMISSIONS'
  | 'HOSPITAL_DEPARTMENTS'
  | 'APPOINTMENT_POLICIES'
  | 'AUDIT_LOGS'
  | (string & {});

export type AuditActionCategory = 'AUTH' | 'RBAC' | 'CONFIG';

export interface GetSystemAdminDashboardParams {
  recentAuditLimit?: number;
  hospitalId?: number | null;
}

export interface SystemAdminProfileDto {
  userId: number;
  email: string;
  fullName: string;
  staffCode: string | null;
  hospitalId: number | null;
  hospitalName: string;
  roles: string[];
  permissions: string[];
}

export interface SystemAdminMetricsDto {
  totalUsers: number;
  activeUsers: number;
  lockedOrInactiveUsers: number;
  totalStaffAccounts: number;
  activeStaffAccounts: number;
  inactiveOrLockedStaffAccounts: number;
  totalRoles: number;
  totalPermissions: number;
  totalHospitals: number;
  activeDepartments: number;
  totalAuditLogs: number;
  recentAuditCount24h: number;
}

export interface SystemAdminAllowedModuleDto {
  moduleCode: SystemAdminModuleCode;
  title: string;
  description: string;
  route: string;
  requiredPermission: SystemAdminPermissionCode;
  badgeCount: number | null;
}

export interface SystemAdminRoleSummaryDto {
  roleId: number;
  code: string;
  name: string;
  assignedUserCount: number;
  activeUserCount: number;
  permissionCount: number;
  permissions: string[];
}

export interface SystemAdminAuditLogItemDto {
  id: number;
  actorUserId: number | null;
  actorEmail: string | null;
  hospitalId: number | null;
  action: string;
  entityType: string;
  entityId: string | null;
  reason: string | null;
  ipAddress: string | null;
  occurredAt: string;
}

export interface SystemAdminDashboardResponseDto {
  adminProfile: SystemAdminProfileDto;
  metrics: SystemAdminMetricsDto;
  allowedModules: SystemAdminAllowedModuleDto[];
  roleSummaries: SystemAdminRoleSummaryDto[];
  recentAuditLogs: SystemAdminAuditLogItemDto[];
  generatedAtUtc: string;
}
