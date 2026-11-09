import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { ROLE_METRICS_MOCK, ROLES_MOCK_DATA } from '../data/role-mock-data';
import type {
  GetRoleListParams,
  RoleCatalogResult,
  RoleItem,
  RoleListItemDto,
  RoleListResponseDto,
  RoleMetrics,
  RoleMetricsDto,
} from '../types/role.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return (
    error.response.status === 404 || error.response.status === 502 || error.response.status === 503
  );
}

export function shouldUseRoleHttpApi(): boolean {
  if (import.meta.env.MODE !== 'test') return true;
  return 'mock' in apiClient.get;
}

export function mapRoleItemDto(dto: RoleListItemDto): RoleItem {
  return {
    roleId: dto.roleId,
    id: dto.id || `ROL-${String(dto.roleId).padStart(2, '0')}`,
    code: dto.code,
    name: dto.name,
    description: dto.description,
    scope: dto.scope,
    isSystemRole: dto.isSystemRole,
    assignedUsersCount: dto.assignedUsersCount,
    activeUsersCount: dto.activeUsersCount,
    permissionCount: dto.permissionCount ?? dto.permissions.length,
    permissions: dto.permissions.map((perm) => ({
      permissionId: perm.permissionId,
      code: perm.code,
      name: perm.name,
      module: perm.module,
    })),
    status: dto.status,
    updatedAt: dto.updatedAt,
    segregationNote: dto.segregationNote,
  };
}

export function mapRoleMetricsDto(dto: RoleMetricsDto): RoleMetrics {
  return {
    totalRoles: dto.totalRoles,
    activeRoles: dto.activeRoles,
    inactiveRoles: dto.inactiveRoles,
    systemRoles: dto.systemRoles,
    customRoles: dto.customRoles,
    totalAssignedAccounts: dto.totalAssignedAccounts,
    activeAssignedAccounts: dto.activeAssignedAccounts,
    totalPermissions: dto.totalPermissions,
  };
}

/**
 * Lấy danh sách vai trò hệ thống từ `GET /api/v1/admin/roles`.
 * Tự động fallback sang `ROLES_MOCK_DATA` khi chạy test không mock HTTP hoặc khi backend offline.
 */
export async function fetchRoleListApi(params: GetRoleListParams = {}): Promise<RoleCatalogResult> {
  if (shouldUseRoleHttpApi()) {
    try {
      const queryParams: Record<string, string | number | boolean> = {};
      if (params.keyword) queryParams.keyword = params.keyword;
      if (params.query) queryParams.query = params.query;
      if (params.scope) queryParams.scope = params.scope;
      if (params.status) queryParams.status = params.status;
      if (typeof params.isSystemRole === 'boolean') {
        queryParams.isSystemRole = params.isSystemRole;
      }
      if (params.page) queryParams.page = params.page;
      if (params.pageSize) queryParams.pageSize = params.pageSize;

      const { data } = await apiClient.get<RoleListResponseDto>('/admin/roles', {
        params: queryParams,
      });
      const rawItems = data.data ?? data.items ?? [];
      const items = rawItems.map(mapRoleItemDto);
      return {
        items,
        metrics: mapRoleMetricsDto(data.metrics),
        total: data.total ?? items.length,
        page: data.page ?? 1,
        pageSize: data.pageSize ?? (items.length || 10),
        totalPages: data.totalPages ?? 1,
      };
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return {
    items: ROLES_MOCK_DATA,
    metrics: ROLE_METRICS_MOCK,
    total: ROLES_MOCK_DATA.length,
    page: 1,
    pageSize: ROLES_MOCK_DATA.length,
    totalPages: 1,
  };
}

/**
 * Lấy chi tiết một vai trò theo `roleId` từ `GET /api/v1/admin/roles/{id}`.
 */
export async function fetchRoleByIdApi(roleId: number): Promise<RoleItem> {
  const { data } = await apiClient.get<RoleListItemDto>(`/admin/roles/${roleId}`);
  return mapRoleItemDto(data);
}

export { createRoleApi, updateRoleApi } from './role-permissions.api';
