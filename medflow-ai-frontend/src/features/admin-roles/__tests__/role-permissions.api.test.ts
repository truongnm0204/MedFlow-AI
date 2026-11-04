import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  assignRolePermissionsApi,
  fetchRolesPermissionsCatalogApi,
} from '../api/role-permissions.api';
import {
  getMockRolesCatalog,
  resetMockRolesCatalog,
  SYSTEM_PERMISSIONS,
} from '../data/role-mock-data';
import { assignPermissionsSchema } from '../schemas/assign-permissions.schema';
import type { RolesPermissionsCatalogDto } from '../types/role.types';

function makeAxiosError(status: number, detail: string): AxiosError {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  const response: AxiosResponse = {
    data: { detail },
    status,
    statusText: String(status),
    headers: {},
    config,
  };
  return new AxiosError(detail, `ERR_BAD_REQUEST_${status}`, config, {}, response);
}

describe('assignPermissionsSchema (SCR-ADM-ROLE-02)', () => {
  it('accepts valid permission assignments and optional audit note', () => {
    const parsed = assignPermissionsSchema.safeParse({
      roleId: 'ROL-03',
      roleCode: 'DOCTOR',
      permissionCodes: ['PATIENT_PROFILE_READ', 'MEDICAL_RECORD_WRITE', 'PRESCRIPTION_ISSUE'],
      note: 'Điều chỉnh phân quyền bác sĩ khám ngoại trú',
    });
    expect(parsed.success).toBe(true);
  });

  it('rejects empty permissionCodes array', () => {
    const parsed = assignPermissionsSchema.safeParse({
      roleId: 'ROL-03',
      roleCode: 'DOCTOR',
      permissionCodes: [],
    });
    expect(parsed.success).toBe(false);
    if (!parsed.success) {
      expect(parsed.error.issues[0]?.message).toBe(
        'Vai trò phải được gán ít nhất 1 quyền hạn hoạt động.',
      );
    }
  });

  it('rejects revoking USER_MANAGE from SYSTEM_ADMIN (self-lockout protection)', () => {
    const parsed = assignPermissionsSchema.safeParse({
      roleId: 'ROL-01',
      roleCode: 'SYSTEM_ADMIN',
      permissionCodes: ['HOSPITAL_CONFIG', 'AUDIT_READ'],
    });
    expect(parsed.success).toBe(false);
    if (!parsed.success) {
      expect(parsed.error.issues[0]?.message).toBe(
        'Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.',
      );
    }
  });

  it('rejects simultaneous PRESCRIPTION_ISSUE and MEDICINE_DISPENSE (SoD violation)', () => {
    const parsed = assignPermissionsSchema.safeParse({
      roleId: 'ROL-03',
      roleCode: 'DOCTOR',
      permissionCodes: ['PATIENT_PROFILE_READ', 'PRESCRIPTION_ISSUE', 'MEDICINE_DISPENSE'],
    });
    expect(parsed.success).toBe(false);
    if (!parsed.success) {
      expect(parsed.error.issues[0]?.message).toBe(
        'Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.',
      );
    }
  });
});

describe('role-permissions.api (SCR-ADM-ROLE-02)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    window.localStorage.setItem('medflow.access_token', 'test-jwt-token');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
  });

  it('fetchRolesPermissionsCatalogApi returns server catalog on HTTP 200', async () => {
    const serverCatalog: RolesPermissionsCatalogDto = getMockRolesCatalog();
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: serverCatalog });

    const result = await fetchRolesPermissionsCatalogApi();

    expect(getSpy).toHaveBeenCalledWith('/admin/roles');
    expect(result).toEqual(serverCatalog);
  });

  it('fetchRolesPermissionsCatalogApi falls back to mock catalog on HTTP 404 or network error', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));

    const result404 = await fetchRolesPermissionsCatalogApi();
    expect(result404).toEqual(getMockRolesCatalog());

    const networkErr = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkErr);

    const resultNetwork = await fetchRolesPermissionsCatalogApi();
    expect(resultNetwork).toEqual(getMockRolesCatalog());
  });

  it('fetchRolesPermissionsCatalogApi rethrows AxiosError on HTTP 403 without falling back to mock', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(
      makeAxiosError(403, 'Không đủ thẩm quyền truy cập danh mục vai trò.'),
    );

    await expect(fetchRolesPermissionsCatalogApi()).rejects.toThrow(AxiosError);
  });

  it('assignRolePermissionsApi sends PUT /admin/roles/:roleId/permissions and updates mock state on 404 fallback', async () => {
    const putSpy = vi
      .spyOn(apiClient, 'put')
      .mockRejectedValueOnce(makeAxiosError(404, 'Endpoint not yet deployed'));

    const nextCodes = ['PATIENT_PROFILE_READ', 'QUEUE_MANAGE', 'MEDICAL_RECORD_WRITE'];
    const updatedRole = await assignRolePermissionsApi({
      roleId: 'ROL-03',
      roleCode: 'DOCTOR',
      permissionCodes: nextCodes,
      note: 'Cập nhật quyền bác sĩ',
    });

    expect(putSpy).toHaveBeenCalledWith('/admin/roles/ROL-03/permissions', {
      roleCode: 'DOCTOR',
      permissionCodes: nextCodes,
      note: 'Cập nhật quyền bác sĩ',
    });
    expect(updatedRole.permissions.map((p) => p.code)).toEqual(nextCodes);
    expect(updatedRole.updatedAt).toBe('Hôm nay');
    expect(updatedRole.permissions[1]).toEqual(SYSTEM_PERMISSIONS.QUEUE_MANAGE);
  });

  it('assignRolePermissionsApi rethrows HTTP 400 and HTTP 403 ProblemDetails without falling back to mock', async () => {
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(
      makeAxiosError(400, 'Danh sách mã quyền hạn không hợp lệ.'),
    );

    await expect(
      assignRolePermissionsApi({
        roleId: 'ROL-03',
        roleCode: 'DOCTOR',
        permissionCodes: ['PATIENT_PROFILE_READ'],
      }),
    ).rejects.toThrow(AxiosError);

    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(
      makeAxiosError(403, 'Chỉ SYSTEM_ADMIN mới được phép thay đổi ma trận phân quyền.'),
    );

    await expect(
      assignRolePermissionsApi({
        roleId: 'ROL-03',
        roleCode: 'DOCTOR',
        permissionCodes: ['PATIENT_PROFILE_READ'],
      }),
    ).rejects.toThrow(AxiosError);
  });
});
