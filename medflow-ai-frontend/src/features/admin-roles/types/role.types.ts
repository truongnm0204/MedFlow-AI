export type RoleStatus = 'ACTIVE' | 'INACTIVE';

export type RoleScope = 'SYSTEM' | 'CLINICAL' | 'OPERATIONAL' | 'PORTAL';

export interface RolePermissionItem {
  code: string;
  name: string;
  module: string;
}

export interface RoleItem {
  id: string;
  code: string;
  name: string;
  description: string;
  scope: RoleScope;
  isSystemRole: boolean;
  assignedUsersCount: number;
  permissions: RolePermissionItem[];
  status: RoleStatus;
  updatedAt: string;
  segregationNote: string;
}

export interface RoleFilters {
  query: string;
  scope: '' | RoleScope;
  status: '' | RoleStatus;
}

export interface RoleMetrics {
  totalRoles: number;
  activeRoles: number;
  totalAssignedAccounts: number;
  totalPermissions: number;
}
