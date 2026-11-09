import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import * as rolePermissionsApi from '../api/role-permissions.api';
import { updateRoleApi } from '../api/role-permissions.api';
import { resetMockRolesCatalog } from '../data/role-mock-data';
import { RoleListPage } from '../pages/role-list-page';
import { updateRoleSchema } from '../schemas/update-role.schema';

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

function renderRoleListPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/roles']}>
        <RoleListPage initialView="list" />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('updateRoleSchema (SCR-ADM-ROLE-01 / UC58)', () => {
  it('accepts valid updates for system and custom roles', () => {
    const systemParsed = updateRoleSchema.safeParse({
      roleId: 'ROL-03',
      originalCode: 'DOCTOR',
      isSystemRole: true,
      code: 'doctor',
      name: '  Bác sĩ khám bệnh ngoại trú  ',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: '  Khám lâm sàng, chỉ định CLS và kê đơn thuốc ngoại trú.  ',
      segregationNote: '  Tách biệt với quyền cấp phát thuốc tại nhà thuốc.  ',
      note: '  Chuẩn hóa tên hiển thị vai trò bác sĩ  ',
    });

    expect(systemParsed.success).toBe(true);
    if (systemParsed.success) {
      expect(systemParsed.data.code).toBe('DOCTOR');
      expect(systemParsed.data.name).toBe('Bác sĩ khám bệnh ngoại trú');
      expect(systemParsed.data.note).toBe('Chuẩn hóa tên hiển thị vai trò bác sĩ');
    }

    const customParsed = updateRoleSchema.safeParse({
      roleId: 'ROL-10',
      originalCode: 'SECURITY_AUDITOR',
      isSystemRole: false,
      code: 'compliance_auditor',
      name: 'Kiểm toán viên tuân thủ',
      scope: 'OPERATIONAL',
      status: 'INACTIVE',
      description: 'Kiểm tra định kỳ quy trình vận hành và bảo mật hồ sơ.',
      segregationNote: 'Chỉ đọc dữ liệu kiểm toán, không thao tác nghiệp vụ.',
    });

    expect(customParsed.success).toBe(true);
    if (customParsed.success) {
      expect(customParsed.data.code).toBe('COMPLIANCE_AUDITOR');
      expect(customParsed.data.status).toBe('INACTIVE');
    }
  });

  it('rejects invalid code token, out-of-range name, empty description/SoD, and long note', () => {
    const invalidCode = updateRoleSchema.safeParse({
      roleId: 'ROL-08',
      originalCode: 'HEAD_DOCTOR',
      isSystemRole: false,
      code: '1_INVALID_CODE',
      name: 'Trưởng khoa',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(invalidCode.success).toBe(false);
    if (!invalidCode.success) {
      expect(invalidCode.error.issues[0]?.message).toBe(
        'Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).',
      );
    }

    const shortName = updateRoleSchema.safeParse({
      roleId: 'ROL-08',
      originalCode: 'HEAD_DOCTOR',
      isSystemRole: false,
      code: 'HEAD_DOCTOR',
      name: 'A',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(shortName.success).toBe(false);
    if (!shortName.success) {
      expect(shortName.error.issues[0]?.message).toBe('Tên vai trò phải từ 2 đến 120 ký tự.');
    }

    const emptyDescription = updateRoleSchema.safeParse({
      roleId: 'ROL-08',
      originalCode: 'HEAD_DOCTOR',
      isSystemRole: false,
      code: 'HEAD_DOCTOR',
      name: 'Trưởng khoa',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: '   ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(emptyDescription.success).toBe(false);
    if (!emptyDescription.success) {
      expect(emptyDescription.error.issues[0]?.message).toBe('Mô tả vai trò không được để trống.');
    }

    const longNote = updateRoleSchema.safeParse({
      roleId: 'ROL-08',
      originalCode: 'HEAD_DOCTOR',
      isSystemRole: false,
      code: 'HEAD_DOCTOR',
      name: 'Trưởng khoa',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
      note: 'N'.repeat(251),
    });
    expect(longNote.success).toBe(false);
    if (!longNote.success) {
      expect(longNote.error.issues[0]?.message).toBe(
        'Ghi chú kiểm toán không được vượt quá 250 ký tự.',
      );
    }
  });

  it('enforces system role protection rules (code immutable, ACTIVE status locked, SYSTEM_ADMIN scope locked)', () => {
    const codeChangedOnSystemRole = updateRoleSchema.safeParse({
      roleId: 'ROL-03',
      originalCode: 'DOCTOR',
      isSystemRole: true,
      code: 'SENIOR_DOCTOR',
      name: 'Bác sĩ lâm sàng',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(codeChangedOnSystemRole.success).toBe(false);
    if (!codeChangedOnSystemRole.success) {
      expect(codeChangedOnSystemRole.error.issues[0]?.message).toBe(
        'Không được phép thay đổi mã định danh (code) của vai trò hệ thống mặc định.',
      );
    }

    const inactiveSystemAdmin = updateRoleSchema.safeParse({
      roleId: 'ROL-01',
      originalCode: 'SYSTEM_ADMIN',
      isSystemRole: true,
      code: 'SYSTEM_ADMIN',
      name: 'Quản trị hệ thống',
      scope: 'SYSTEM',
      status: 'INACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(inactiveSystemAdmin.success).toBe(false);
    if (!inactiveSystemAdmin.success) {
      expect(inactiveSystemAdmin.error.issues[0]?.message).toBe(
        'Không thể ngừng hoạt động vai trò SYSTEM_ADMIN để tránh khóa quyền quản trị hệ thống.',
      );
    }

    const inactiveDoctor = updateRoleSchema.safeParse({
      roleId: 'ROL-03',
      originalCode: 'DOCTOR',
      isSystemRole: true,
      code: 'DOCTOR',
      name: 'Bác sĩ lâm sàng',
      scope: 'CLINICAL',
      status: 'INACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(inactiveDoctor.success).toBe(false);
    if (!inactiveDoctor.success) {
      expect(inactiveDoctor.error.issues[0]?.message).toBe(
        'Không thể ngừng hoạt động vai trò hệ thống mặc định.',
      );
    }

    const wrongScopeSystemAdmin = updateRoleSchema.safeParse({
      roleId: 'ROL-01',
      originalCode: 'SYSTEM_ADMIN',
      isSystemRole: true,
      code: 'SYSTEM_ADMIN',
      name: 'Quản trị hệ thống',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      description: 'Mô tả hợp lệ',
      segregationNote: 'Ghi chú SoD hợp lệ',
    });
    expect(wrongScopeSystemAdmin.success).toBe(false);
    if (!wrongScopeSystemAdmin.success) {
      expect(wrongScopeSystemAdmin.error.issues[0]?.message).toBe(
        'Vai trò SYSTEM_ADMIN bắt buộc thuộc nhóm phân hệ SYSTEM.',
      );
    }
  });
});

describe('updateRoleApi (SCR-ADM-ROLE-01 / UC58)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('sends PUT /admin/roles/:roleId with normalized payload and maps RoleListItemDto on HTTP 200', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        roleId: 8,
        id: 'ROL-08',
        code: 'CHIEF_PHYSICIAN',
        name: 'Trưởng khoa khám bệnh',
        description: 'Điều hành chuyên môn toàn khoa khám bệnh và hội chẩn.',
        scope: 'CLINICAL',
        isSystemRole: false,
        assignedUsersCount: 6,
        activeUsersCount: 6,
        permissionCount: 2,
        permissions: [
          {
            permissionId: 1,
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
        segregationNote: 'Giới hạn phê duyệt trong phạm vi chuyên khoa.',
      },
    });

    const result = await updateRoleApi({
      roleId: 'ROL-08',
      code: '  chief_physician  ',
      name: '  Trưởng khoa khám bệnh  ',
      description: '  Điều hành chuyên môn toàn khoa khám bệnh và hội chẩn.  ',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      segregationNote: '  Giới hạn phê duyệt trong phạm vi chuyên khoa.  ',
      note: '  Đổi mã và tên vai trò trưởng khoa  ',
    });

    expect(putSpy).toHaveBeenCalledWith('/admin/roles/ROL-08', {
      code: 'CHIEF_PHYSICIAN',
      name: 'Trưởng khoa khám bệnh',
      description: 'Điều hành chuyên môn toàn khoa khám bệnh và hội chẩn.',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      segregationNote: 'Giới hạn phê duyệt trong phạm vi chuyên khoa.',
      note: 'Đổi mã và tên vai trò trưởng khoa',
    });
    expect(result.roleId).toBe(8);
    expect(result.id).toBe('ROL-08');
    expect(result.code).toBe('CHIEF_PHYSICIAN');
    expect(result.name).toBe('Trưởng khoa khám bệnh');
    expect(result.updatedAt).toBe('20/04/2026');
  });

  it('falls back to updateMockRole when PUT /admin/roles/:roleId returns 404', async () => {
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));

    const updated = await updateRoleApi({
      roleId: 'ROL-08',
      code: 'DEPT_LEAD_DOCTOR',
      name: 'Bác sĩ trưởng khoa',
      description: 'Quản lý chuyên môn khoa lâm sàng.',
      scope: 'CLINICAL',
      status: 'INACTIVE',
      segregationNote: 'Không kiêm nhiệm cấp phát thuốc.',
    });

    expect(updated.id).toBe('ROL-08');
    expect(updated.code).toBe('DEPT_LEAD_DOCTOR');
    expect(updated.name).toBe('Bác sĩ trưởng khoa');
    expect(updated.status).toBe('INACTIVE');
    expect(updated.updatedAt).toBe('Hôm nay');
  });

  it('rethrows AxiosError on HTTP 400, 403, and 409 without falling back to mock', async () => {
    for (const [status, detail] of [
      [400, 'Không thể ngừng hoạt động vai trò hệ thống mặc định.'],
      [403, 'Chỉ Quản trị viên hệ thống (SYSTEM_ADMIN) mới có quyền cập nhật vai trò.'],
      [409, 'Tên vai trò đã tồn tại trong danh mục hệ thống.'],
    ] as const) {
      vi.spyOn(apiClient, 'put').mockRejectedValueOnce(makeAxiosError(status, detail));

      await expect(
        updateRoleApi({
          roleId: 'ROL-03',
          code: 'DOCTOR',
          name: 'Bác sĩ lâm sàng',
          description: 'Khám bệnh và kê đơn.',
          scope: 'CLINICAL',
          status: 'ACTIVE',
          segregationNote: 'Tách biệt với cấp phát thuốc.',
        }),
      ).rejects.toThrow(AxiosError);
    }
  });
});

describe('Update Role UI (SCR-ADM-ROLE-01 / UC58)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    useAuthStore.getState().clearAuth();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    useAuthStore.getState().clearAuth();
  });

  it('updates a system role metadata, closes modal, updates table row, and shows status alert', async () => {
    const updateSpy = vi.spyOn(rolePermissionsApi, 'updateRoleApi');
    renderRoleListPage();

    fireEvent.click(screen.getByRole('button', { name: 'Chỉnh sửa Bác sĩ lâm sàng' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Cập nhật thông tin vai trò')).toBeInTheDocument();

    const codeInput = within(dialog).getByLabelText('Mã vai trò') as HTMLInputElement;
    const statusSelect = within(dialog).getByLabelText('Trạng thái vai trò') as HTMLSelectElement;
    const scopeSelect = within(dialog).getByLabelText('Nhóm phân hệ vai trò') as HTMLSelectElement;
    const nameInput = within(dialog).getByLabelText('Tên vai trò') as HTMLInputElement;
    const descriptionInput = within(dialog).getByLabelText('Mô tả vai trò') as HTMLTextAreaElement;
    const sodInput = within(dialog).getByLabelText(
      'Ghi chú phân tách nhiệm vụ (SoD)',
    ) as HTMLTextAreaElement;
    const noteInput = within(dialog).getByLabelText(
      'Ghi chú kiểm toán cập nhật',
    ) as HTMLInputElement;

    expect(codeInput.disabled).toBe(true);
    expect(statusSelect.disabled).toBe(true);
    expect(scopeSelect.disabled).toBe(false);

    fireEvent.change(nameInput, { target: { value: 'Bác sĩ khám bệnh ngoại trú' } });
    fireEvent.change(descriptionInput, {
      target: { value: 'Khám lâm sàng, chỉ định cận lâm sàng và kê đơn ngoại trú.' },
    });
    fireEvent.change(sodInput, {
      target: { value: 'Tách biệt hoàn toàn với quyền cấp phát thuốc nhà thuốc.' },
    });
    fireEvent.change(noteInput, { target: { value: 'Chuẩn hóa tên vai trò bác sĩ' } });

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu thay đổi/i }));

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith({
        roleId: 'ROL-03',
        code: 'DOCTOR',
        name: 'Bác sĩ khám bệnh ngoại trú',
        scope: 'CLINICAL',
        status: 'ACTIVE',
        description: 'Khám lâm sàng, chỉ định cận lâm sàng và kê đơn ngoại trú.',
        segregationNote: 'Tách biệt hoàn toàn với quyền cấp phát thuốc nhà thuốc.',
        note: 'Chuẩn hóa tên vai trò bác sĩ',
      });
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(
      screen.getByText(
        'Đã cập nhật thông tin vai trò Bác sĩ khám bệnh ngoại trú (DOCTOR) thành công.',
      ),
    ).toBeInTheDocument();

    const updatedRow = screen.getByText('Bác sĩ khám bệnh ngoại trú').closest('tr');
    expect(updatedRow).not.toBeNull();
    if (updatedRow) {
      expect(
        within(updatedRow).getByText('Khám lâm sàng, chỉ định cận lâm sàng và kê đơn ngoại trú.'),
      ).toBeInTheDocument();
    }
  });

  it('updates a custom role code, scope, and status and refreshes active role metrics', async () => {
    const updateSpy = vi.spyOn(rolePermissionsApi, 'updateRoleApi');
    renderRoleListPage();

    // Filter to find custom role SECURITY_AUDITOR (ROL-10, initially INACTIVE)
    const searchInput = screen.getByLabelText('Tìm kiếm vai trò');
    fireEvent.change(searchInput, { target: { value: 'SECURITY_AUDITOR' } });

    fireEvent.click(screen.getByRole('button', { name: 'Chỉnh sửa Kiểm toán viên an ninh' }));

    const dialog = screen.getByRole('dialog');
    const codeInput = within(dialog).getByLabelText('Mã vai trò') as HTMLInputElement;
    const nameInput = within(dialog).getByLabelText('Tên vai trò') as HTMLInputElement;
    const scopeSelect = within(dialog).getByLabelText('Nhóm phân hệ vai trò') as HTMLSelectElement;
    const statusSelect = within(dialog).getByLabelText('Trạng thái vai trò') as HTMLSelectElement;

    expect(codeInput.disabled).toBe(false);
    expect(statusSelect.disabled).toBe(false);

    fireEvent.change(codeInput, { target: { value: 'compliance_auditor' } });
    fireEvent.change(nameInput, { target: { value: 'Kiểm toán viên tuân thủ' } });
    fireEvent.change(scopeSelect, { target: { value: 'OPERATIONAL' } });
    fireEvent.change(statusSelect, { target: { value: 'ACTIVE' } });

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu thay đổi/i }));

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          roleId: 'ROL-10',
          code: 'COMPLIANCE_AUDITOR',
          name: 'Kiểm toán viên tuân thủ',
          scope: 'OPERATIONAL',
          status: 'ACTIVE',
        }),
      );
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    // Clear search filter and verify updated custom role code & name
    fireEvent.change(searchInput, { target: { value: 'COMPLIANCE_AUDITOR' } });
    expect(screen.getByText('COMPLIANCE_AUDITOR')).toBeInTheDocument();
    expect(screen.getByText('Kiểm toán viên tuân thủ')).toBeInTheDocument();
  });

  it('locks code, scope, and status fields when editing SYSTEM_ADMIN and supports opening edit modal from RoleDetailModal', () => {
    renderRoleListPage();

    fireEvent.click(screen.getByRole('button', { name: 'Xem chi tiết Quản trị hệ thống' }));
    const detailDialog = screen.getByRole('dialog');
    fireEvent.click(within(detailDialog).getByRole('button', { name: 'Chỉnh sửa vai trò' }));

    const editDialog = screen.getByRole('dialog');
    expect(within(editDialog).getByText('Cập nhật thông tin vai trò')).toBeInTheDocument();

    const codeInput = within(editDialog).getByLabelText('Mã vai trò') as HTMLInputElement;
    const scopeSelect = within(editDialog).getByLabelText(
      'Nhóm phân hệ vai trò',
    ) as HTMLSelectElement;
    const statusSelect = within(editDialog).getByLabelText(
      'Trạng thái vai trò',
    ) as HTMLSelectElement;

    expect(codeInput.disabled).toBe(true);
    expect(scopeSelect.disabled).toBe(true);
    expect(statusSelect.disabled).toBe(true);
  });

  it('blocks form submission and displays validation error when role name is cleared', async () => {
    const updateSpy = vi.spyOn(rolePermissionsApi, 'updateRoleApi');
    renderRoleListPage();

    fireEvent.click(screen.getByRole('button', { name: 'Chỉnh sửa Bác sĩ lâm sàng' }));
    const dialog = screen.getByRole('dialog');

    const nameInput = within(dialog).getByLabelText('Tên vai trò');
    fireEvent.change(nameInput, { target: { value: '   ' } });

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu thay đổi/i }));

    expect(await within(dialog).findByText('Tên vai trò không được để trống.')).toBeInTheDocument();
    expect(updateSpy).not.toHaveBeenCalled();
  });

  it('displays server ProblemDetails error inside modal alert and keeps modal open on 409 Conflict', async () => {
    vi.spyOn(rolePermissionsApi, 'updateRoleApi').mockRejectedValueOnce(
      makeAxiosError(409, 'Tên vai trò đã tồn tại trong danh mục hệ thống.'),
    );

    renderRoleListPage();

    fireEvent.click(screen.getByRole('button', { name: 'Chỉnh sửa Trưởng khoa lâm sàng' }));
    const dialog = screen.getByRole('dialog');

    const nameInput = within(dialog).getByLabelText('Tên vai trò');
    fireEvent.change(nameInput, { target: { value: 'Bác sĩ lâm sàng' } });

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu thay đổi/i }));

    expect(
      await within(dialog).findByText('Tên vai trò đã tồn tại trong danh mục hệ thống.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('hides Chỉnh sửa buttons when logged in as non-SYSTEM_ADMIN (HOSPITAL_ADMIN)', () => {
    useAuthStore.setState({
      user: {
        id: 2,
        email: 'hospital.admin@medflow.vn',
        fullName: 'Quản trị bệnh viện',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-002',
      },
      isAuthenticated: true,
    });

    renderRoleListPage();

    expect(screen.queryByRole('button', { name: /Chỉnh sửa/i })).toBeNull();
  });
});
