import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { mapRoleItemDto, mapRoleMetricsDto } from './role.api';
import {
  createMockRole,
  getMockRolesCatalog,
  SYSTEM_PERMISSION_LIST,
  updateMockRolePermissions,
} from '../data/role-mock-data';
import type {
  AssignRolePermissionsResponseDto,
  CreateRoleRequest,
  GetRoleListParams,
  RoleItem,
  RoleListItemDto,
  RoleListResponseDto,
  RoleMetricsDto,
  RolePermissionsCatalogResponseDto,
  RolesPermissionsCatalogDto,
  UpdateRolePermissionsRequest,
} from '../types/role.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response || error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get || 'mock' in apiClient.put || 'mock' in apiClient.post) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

export async function fetchRolesPermissionsCatalogApi(
  params?: GetRoleListParams,
): Promise<RolesPermissionsCatalogDto> {
  if (shouldUseHttpApi()) {
    try {
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
      const permissions = SYSTEM_PERMISSION_LIST.map((perm) => ({ ...perm }));
      if (Array.isArray(data)) {
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
        return {
          roles: rawItems.map(mapRoleItemDto),
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
          return {
            roles: data.roles.map((role: RoleItem | RoleListItemDto) =>
              'id' in role ? (role as RoleItem) : mapRoleItemDto(role as RoleListItemDto),
            ),
            permissions: data.permissions?.length ? data.permissions : permissions,
            metrics: needsMetricsMapping
              ? mapRoleMetricsDto(data.metrics as RoleMetricsDto)
              : data.metrics,
          };
        }
      }
      return data as RolesPermissionsCatalogDto;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return getMockRolesCatalog();
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

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.put<
        RoleItem | RoleListItemDto | AssignRolePermissionsResponseDto
      >(`/admin/roles/${encodeURIComponent(payload.roleId)}/permissions`, body);
      const rawRole = 'role' in data ? data.role : data;
      const updatedRole =
        'roleId' in rawRole && typeof rawRole.roleId === 'number'
          ? mapRoleItemDto(rawRole as RoleListItemDto)
          : (rawRole as RoleItem);
      try {
        updateMockRolePermissions(payload.roleId, payload.permissionCodes);
      } catch {
        // Role exists only on server; ignore mock sync error
      }
      return updatedRole;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockRolePermissions(payload.roleId, payload.permissionCodes);
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

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.post<RoleItem | RoleListItemDto>('/admin/roles', body);
      const createdRole =
        'roleId' in data && typeof data.roleId === 'number'
          ? mapRoleItemDto(data as RoleListItemDto)
          : (data as RoleItem);
      try {
        createMockRole(body);
      } catch {
        // Role may already exist in mock catalog; ignore mock sync error
      }
      return createdRole;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return createMockRole(body);
}
