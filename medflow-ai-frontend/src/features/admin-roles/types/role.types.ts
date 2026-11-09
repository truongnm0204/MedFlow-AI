export type RoleStatus = 'ACTIVE' | 'INACTIVE';

export type RoleScope = 'SYSTEM' | 'CLINICAL' | 'OPERATIONAL' | 'PORTAL';

export interface RolePermissionItem {
  permissionId?: number;
  code: string;
  name: string;
  module: string;
}

export interface RoleItem {
  roleId?: number;
  id: string;
  code: string;
  name: string;
  description: string;
  scope: RoleScope;
  isSystemRole: boolean;
  assignedUsersCount: number;
  activeUsersCount?: number;
  permissionCount?: number;
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
  inactiveRoles?: number;
  systemRoles?: number;
  customRoles?: number;
  totalAssignedAccounts: number;
  activeAssignedAccounts?: number;
  totalPermissions: number;
}

export type RoleViewMode = 'list' | 'matrix' | 'access-check';

export interface PermissionGroup {
  module: string;
  permissions: RolePermissionItem[];
}

export interface RolesPermissionsCatalogDto {
  roles: RoleItem[];
  permissions: RolePermissionItem[];
  metrics: RoleMetrics;
}

export interface UpdateRolePermissionsRequest {
  roleId: string;
  roleCode: string;
  permissionCodes: string[];
  note?: string;
}

export interface CreateRoleRequest {
  code: string;
  name: string;
  description?: string;
  scope?: RoleScope;
  status?: RoleStatus;
  segregationNote?: string;
  permissionCodes?: string[];
  note?: string;
}

export interface AssignRolePermissionsResponseDto {
  role: RoleItem;
  updatedAt: string;
}

export interface GetRoleListParams {
  keyword?: string;
  query?: string;
  scope?: RoleScope | '';
  status?: RoleStatus | '';
  isSystemRole?: boolean;
  page?: number;
  pageSize?: number;
}

export interface RolePermissionItemDto {
  permissionId?: number;
  code: string;
  name: string;
  module: string;
}

export interface RoleListItemDto {
  roleId: number;
  id: string;
  code: string;
  name: string;
  description: string;
  scope: RoleScope;
  isSystemRole: boolean;
  assignedUsersCount: number;
  activeUsersCount: number;
  permissionCount: number;
  permissions: RolePermissionItemDto[];
  status: RoleStatus;
  updatedAt: string;
  segregationNote: string;
}

export interface RoleMetricsDto {
  totalRoles: number;
  activeRoles: number;
  inactiveRoles: number;
  systemRoles: number;
  customRoles: number;
  totalAssignedAccounts: number;
  activeAssignedAccounts: number;
  totalPermissions: number;
}

export interface RoleListResponseDto {
  data: RoleListItemDto[];
  items?: RoleListItemDto[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  metrics: RoleMetricsDto;
}

export interface RolePermissionsCatalogResponseDto {
  roles: (RoleItem | RoleListItemDto)[];
  permissions: RolePermissionItemDto[];
  metrics: RoleMetrics | RoleMetricsDto;
}

export interface RoleCatalogResult {
  items: RoleItem[];
  metrics: RoleMetrics;
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
