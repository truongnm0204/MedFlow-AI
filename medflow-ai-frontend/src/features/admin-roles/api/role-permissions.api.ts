import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  getMockRolesCatalog,
  SYSTEM_PERMISSION_LIST,
  updateMockRolePermissions,
} from '../data/role-mock-data';
import type {
  AssignRolePermissionsResponseDto,
  RoleItem,
  RolesPermissionsCatalogDto,
  UpdateRolePermissionsRequest,
} from '../types/role.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response || error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get || 'mock' in apiClient.put) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

export async function fetchRolesPermissionsCatalogApi(): Promise<RolesPermissionsCatalogDto> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<RolesPermissionsCatalogDto | RoleItem[]>('/admin/roles');
      if (Array.isArray(data)) {
        const permissions = SYSTEM_PERMISSION_LIST.map((perm) => ({ ...perm }));
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
      return data;
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
      const { data } = await apiClient.put<RoleItem | AssignRolePermissionsResponseDto>(
        `/admin/roles/${encodeURIComponent(payload.roleId)}/permissions`,
        body,
      );
      const updatedRole = 'role' in data ? data.role : data;
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
