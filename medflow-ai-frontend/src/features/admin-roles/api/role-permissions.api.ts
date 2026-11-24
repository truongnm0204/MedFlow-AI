import { apiClient } from '@/shared/lib/axios';

import { mapRoleItemDto, mapRoleMetricsDto } from './role.api';
import type {
  AssignRolePermissionsResponseDto,
  CreateRoleRequest,
  GetRoleListParams,
  RoleItem,
  RoleListItemDto,
  RoleListResponseDto,
  RoleMetricsDto,
  RolePermissionItem,
  RolePermissionsCatalogResponseDto,
  RolesPermissionsCatalogDto,
  UpdateRolePermissionsRequest,
  UpdateRoleRequest,
} from '../types/role.types';

function derivePermissionsFromRoles(roles: RoleItem[]): RolePermissionItem[] {
  const seen = new Map<RolePermissionItem['code'], RolePermissionItem>();
  for (const role of roles) {
    for (const perm of role.permissions ?? []) {
      if (!seen.has(perm.code)) {
        seen.set(perm.code, { ...perm });
      }
    }
  }
  return [...seen.values()];
}

export async function fetchRolesPermissionsCatalogApi(
  params?: GetRoleListParams,
): Promise<RolesPermissionsCatalogDto> {
  const { data } = params
    ? await apiClient.get<
        | RolesPermissionsCatalogDto
        | RolePermissionsCatalogResponseDto
        | RoleListResponseDto
        | RoleItem[]
      >('/admin/roles', { params })
    : await apiClient.get<
        | RolesPermissionsCatalogDto
        | RolePermissionsCatalogResponseDto
        | RoleListResponseDto
        | RoleItem[]
      >('/admin/roles');

  if (Array.isArray(data)) {
    const permissions = derivePermissionsFromRoles(data);
    return {
      roles: data,
      permissions,
      metrics: {
        totalRoles: data.length,
        activeRoles: data.filter((role) => role.status === 'ACTIVE').length,
        totalAssignedAccounts: data.reduce((sum, role) => sum + role.assignedUsersCount, 0),
        totalPermissions: permissions.length,
      },
    };
  }

  if ('data' in data) {
    const rawItems = data.data ?? data.items ?? [];
    const roles = rawItems.map(mapRoleItemDto);
    const permissions = derivePermissionsFromRoles(roles);
    return {
      roles,
      permissions,
      metrics: mapRoleMetricsDto(data.metrics),
    };
  }

  if ('roles' in data && Array.isArray(data.roles)) {
    const needsRoleMapping = data.roles.some(
      (role: RoleItem | RoleListItemDto) =>
        'roleId' in role && typeof role.roleId === 'number' && !('id' in role),
    );
    const needsMetricsMapping =
      Boolean(data.metrics) &&
      'inactiveRoles' in data.metrics &&
      typeof data.metrics.inactiveRoles === 'number';
    if (needsRoleMapping || needsMetricsMapping) {
      const mappedRoles = data.roles.map((role: RoleItem | RoleListItemDto) =>
        'id' in role ? (role as RoleItem) : mapRoleItemDto(role as RoleListItemDto),
      );
      const permissions = data.permissions?.length
        ? data.permissions
        : derivePermissionsFromRoles(mappedRoles);
      return {
        roles: mappedRoles,
        permissions,
        metrics: needsMetricsMapping
          ? mapRoleMetricsDto(data.metrics as RoleMetricsDto)
          : data.metrics,
      };
    }
  }

  return data as RolesPermissionsCatalogDto;
}

export async function assignRolePermissionsApi(
  payload: UpdateRolePermissionsRequest,
): Promise<RoleItem> {
  const trimmedNote = payload.note?.trim();
  const body = {
    roleCode: payload.roleCode,
    permissionCodes: payload.permissionCodes,
    ...(trimmedNote ? { note: trimmedNote } : {}),
  };

  const { data } = await apiClient.put<
    RoleItem | RoleListItemDto | AssignRolePermissionsResponseDto
  >(`/admin/roles/${encodeURIComponent(payload.roleId)}/permissions`, body);

  const rawRole = 'role' in data ? data.role : data;
  return 'roleId' in rawRole && typeof rawRole.roleId === 'number'
    ? mapRoleItemDto(rawRole as RoleListItemDto)
    : (rawRole as RoleItem);
}

export async function createRoleApi(payload: CreateRoleRequest): Promise<RoleItem> {
  const trimmedDescription = payload.description?.trim();
  const trimmedSegregationNote = payload.segregationNote?.trim();
  const trimmedNote = payload.note?.trim();

  const body: CreateRoleRequest = {
    code: payload.code.trim().toUpperCase(),
    name: payload.name.trim(),
    scope: payload.scope ?? 'OPERATIONAL',
    status: payload.status ?? 'ACTIVE',
    ...(trimmedDescription ? { description: trimmedDescription } : {}),
    ...(trimmedSegregationNote ? { segregationNote: trimmedSegregationNote } : {}),
    ...(payload.permissionCodes ? { permissionCodes: payload.permissionCodes } : {}),
    ...(trimmedNote ? { note: trimmedNote } : {}),
  };

  const { data } = await apiClient.post<RoleItem | RoleListItemDto>('/admin/roles', body);
  return 'roleId' in data && typeof data.roleId === 'number'
    ? mapRoleItemDto(data as RoleListItemDto)
    : (data as RoleItem);
}

export async function updateRoleApi(payload: UpdateRoleRequest): Promise<RoleItem> {
  const trimmedNote = payload.note?.trim();
  const trimmedCode = payload.code?.trim().toUpperCase();
  const body = {
    ...(trimmedCode ? { code: trimmedCode } : {}),
    name: payload.name.trim(),
    description: payload.description.trim(),
    scope: payload.scope,
    status: payload.status,
    segregationNote: payload.segregationNote.trim(),
    ...(payload.permissionCodes ? { permissionCodes: payload.permissionCodes } : {}),
    ...(trimmedNote ? { note: trimmedNote } : {}),
  };

  const { data } = await apiClient.put<RoleItem | RoleListItemDto>(
    `/admin/roles/${encodeURIComponent(payload.roleId)}`,
    body,
  );
  return 'roleId' in data && typeof data.roleId === 'number'
    ? mapRoleItemDto(data as RoleListItemDto)
    : (data as RoleItem);
}
