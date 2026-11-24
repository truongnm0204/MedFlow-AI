import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';

import {
  assignRolePermissionsApi,
  createRoleApi,
  fetchRolesPermissionsCatalogApi,
} from '../api/role-permissions.api';
import { getMockRolesCatalog, resetMockRolesCatalog } from '../data/role-mock-data';
import { assignPermissionsSchema } from '../schemas/assign-permissions.schema';
import { createRoleSchema } from '../schemas/role-create.schema';
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

describe('createRoleSchema (SCR-ADM-ROLE-01 / Issue #235)', () => {
  it('accepts valid custom role input and uppercases role code', () => {
    const parsed = createRoleSchema.safeParse({
      code: '  clinical_coordinator  ',
      name: '  Điều phối viên lâm sàng  ',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Điều phối lịch và hàng đợi khám.',
      segregationNote: 'Không trực tiếp kê đơn thuốc.',
      permissionCodes: ['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN'],
      note: 'Khởi tạo vai trò điều phối',
    });

    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data.code).toBe('CLINICAL_COORDINATOR');
      expect(parsed.data.name).toBe('Điều phối viên lâm sàng');
      expect(parsed.data.permissionCodes).toEqual(['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN']);
    }
  });

  it('allows empty permissionCodes when creating a new role', () => {
    const parsed = createRoleSchema.safeParse({
      code: 'BILLING_REVIEWER',
      name: 'Chuyên viên đối soát viện phí',
    });

    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data.scope).toBe('OPERATIONAL');
      expect(parsed.data.status).toBe('ACTIVE');
      expect(parsed.data.permissionCodes).toEqual([]);
    }
  });

  it('rejects empty or malformed role code and invalid role name length', () => {
    const emptyCode = createRoleSchema.safeParse({
      code: '   ',
      name: 'Vai trò hợp lệ',
    });
    expect(emptyCode.success).toBe(false);
    if (!emptyCode.success) {
      expect(emptyCode.error.issues[0]?.message).toBe('Mã vai trò không được để trống.');
    }

    const invalidCode = createRoleSchema.safeParse({
      code: '123-INVALID',
      name: 'Vai trò hợp lệ',
    });
    expect(invalidCode.success).toBe(false);
    if (!invalidCode.success) {
      expect(invalidCode.error.issues[0]?.message).toBe(
        'Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).',
      );
    }

    const shortName = createRoleSchema.safeParse({
      code: 'CUSTOM_ROLE',
      name: 'A',
    });
    expect(shortName.success).toBe(false);
    if (!shortName.success) {
      expect(shortName.error.issues[0]?.message).toBe('Tên vai trò phải từ 2 đến 120 ký tự.');
    }
  });

  it('rejects simultaneous PRESCRIPTION_ISSUE and MEDICINE_DISPENSE on create (SoD violation)', () => {
    const parsed = createRoleSchema.safeParse({
      code: 'SOD_CONFLICT_ROLE',
      name: 'Vai trò xung đột SoD',
      permissionCodes: ['PRESCRIPTION_ISSUE', 'MEDICINE_DISPENSE'],
    });

    expect(parsed.success).toBe(false);
    if (!parsed.success) {
      expect(parsed.error.issues[0]?.message).toBe(
        'Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.',
      );
    }
  });
});

describe('role-permissions.api (SCR-ADM-ROLE-02 — pure HTTP)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    window.localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');
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

  it('fetchRolesPermissionsCatalogApi derives permissions solely from backend roles and never injects static permissions on empty response', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        data: [],
        metrics: {
          totalRoles: 0,
          activeRoles: 0,
          inactiveRoles: 0,
          totalAssignedAccounts: 0,
          totalPermissions: 0,
        },
      },
    });

    const emptyResult = await fetchRolesPermissionsCatalogApi();
    expect(emptyResult.roles).toEqual([]);
    expect(emptyResult.permissions).toEqual([]);
  });

  it('fetchRolesPermissionsCatalogApi rethrows HTTP 404, 403 and network errors without falling back to mock catalog', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));
    await expect(fetchRolesPermissionsCatalogApi()).rejects.toThrow(AxiosError);

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(
      makeAxiosError(403, 'Không đủ thẩm quyền truy cập danh mục vai trò.'),
    );
    await expect(fetchRolesPermissionsCatalogApi()).rejects.toThrow(AxiosError);

    const networkErr = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkErr);
    await expect(fetchRolesPermissionsCatalogApi()).rejects.toBe(networkErr);
  });

  it('assignRolePermissionsApi normalizes RoleListItemDto on HTTP 200', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        roleId: 3,
        id: 'ROL-03',
        code: 'DOCTOR',
        name: 'Bác sĩ lâm sàng',
        description: 'Thực hiện khám bệnh và kê đơn.',
        scope: 'CLINICAL',
        isSystemRole: true,
        assignedUsersCount: 2,
        activeUsersCount: 2,
        permissionCount: 2,
        permissions: [
          {
            permissionId: 10,
            code: 'PATIENT_PROFILE_READ',
            name: 'Xem hồ sơ bệnh nhân',
            module: 'Hồ sơ & Tiếp đón',
          },
          {
            permissionId: 4,
            code: 'MEDICAL_RECORD_WRITE',
            name: 'Ghi nhận bệnh án lâm sàng',
            module: 'Khám lâm sàng',
          },
        ],
        status: 'ACTIVE',
        updatedAt: '20/04/2026',
        segregationNote: 'Chỉ truy cập hồ sơ bệnh nhân thuộc lượt khám được phân công.',
      },
    });

    const updatedRole = await assignRolePermissionsApi({
      roleId: 'ROL-03',
      roleCode: 'DOCTOR',
      permissionCodes: ['PATIENT_PROFILE_READ', 'MEDICAL_RECORD_WRITE'],
      note: 'Chuẩn hóa phản hồi từ backend',
    });

    expect(putSpy).toHaveBeenCalledWith('/admin/roles/ROL-03/permissions', {
      roleCode: 'DOCTOR',
      permissionCodes: ['PATIENT_PROFILE_READ', 'MEDICAL_RECORD_WRITE'],
      note: 'Chuẩn hóa phản hồi từ backend',
    });
    expect(updatedRole.roleId).toBe(3);
    expect(updatedRole.id).toBe('ROL-03');
    expect(updatedRole.permissionCount).toBe(2);
    expect(updatedRole.permissions.map((p) => p.code)).toEqual([
      'PATIENT_PROFILE_READ',
      'MEDICAL_RECORD_WRITE',
    ]);
    expect(updatedRole.updatedAt).toBe('20/04/2026');
  });

  it('assignRolePermissionsApi rethrows HTTP 400, 403, 404 and network errors without mock fallback', async () => {
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

    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(makeAxiosError(404, 'Role not found'));
    await expect(
      assignRolePermissionsApi({
        roleId: 'ROL-99',
        roleCode: 'DOCTOR',
        permissionCodes: ['PATIENT_PROFILE_READ'],
      }),
    ).rejects.toThrow(AxiosError);
  });

  it('createRoleApi sends POST /admin/roles and normalizes RoleListItemDto on HTTP 201', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        roleId: 11,
        id: 'ROL-11',
        code: 'CLINICAL_COORDINATOR',
        name: 'Điều phối viên lâm sàng',
        description: 'Điều phối lịch và hàng đợi khám.',
        scope: 'CLINICAL',
        isSystemRole: false,
        assignedUsersCount: 0,
        activeUsersCount: 0,
        permissionCount: 2,
        permissions: [
          {
            permissionId: 1,
            code: 'PATIENT_PROFILE_READ',
            name: 'Xem hồ sơ bệnh nhân',
            module: 'Hồ sơ & Tiếp đón',
          },
          {
            permissionId: 3,
            code: 'PATIENT_CHECKIN',
            name: 'Tiếp nhận & Check-in',
            module: 'Tiếp đón',
          },
        ],
        status: 'ACTIVE',
        updatedAt: '09/11/2026',
        segregationNote: 'Không trực tiếp kê đơn thuốc.',
      },
    });

    const created = await createRoleApi({
      code: 'clinical_coordinator',
      name: 'Điều phối viên lâm sàng',
      description: 'Điều phối lịch và hàng đợi khám.',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      segregationNote: 'Không trực tiếp kê đơn thuốc.',
      permissionCodes: ['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN'],
      note: 'Tạo mới vai trò M2',
    });

    expect(postSpy).toHaveBeenCalledWith('/admin/roles', {
      code: 'CLINICAL_COORDINATOR',
      name: 'Điều phối viên lâm sàng',
      description: 'Điều phối lịch và hàng đợi khám.',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      segregationNote: 'Không trực tiếp kê đơn thuốc.',
      permissionCodes: ['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN'],
      note: 'Tạo mới vai trò M2',
    });
    expect(created.roleId).toBe(11);
    expect(created.id).toBe('ROL-11');
    expect(created.code).toBe('CLINICAL_COORDINATOR');
    expect(created.isSystemRole).toBe(false);
    expect(created.permissions.map((p) => p.code)).toEqual([
      'PATIENT_PROFILE_READ',
      'PATIENT_CHECKIN',
    ]);
  });

  it('createRoleApi rethrows HTTP 404, 409 Conflict and network errors without mock role creation', async () => {
    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(makeAxiosError(404, 'Not deployed'));
    await expect(
      createRoleApi({
        code: 'QUALITY_SUPERVISOR',
        name: 'Giám sát chất lượng',
        scope: 'SYSTEM',
        status: 'ACTIVE',
        permissionCodes: ['AUDIT_READ'],
      }),
    ).rejects.toThrow(AxiosError);

    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(
      makeAxiosError(409, "Mã vai trò 'DOCTOR' đã tồn tại trong hệ thống."),
    );
    await expect(
      createRoleApi({
        code: 'DOCTOR',
        name: 'Bác sĩ bản sao',
      }),
    ).rejects.toThrow(AxiosError);
  });
});
