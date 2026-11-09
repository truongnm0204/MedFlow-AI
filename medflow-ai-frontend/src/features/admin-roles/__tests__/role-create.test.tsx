import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { canAccessRoute } from '@/features/auth/utils/rbac';

import * as rolePermissionsApi from '../api/role-permissions.api';
import { resetMockRolesCatalog } from '../data/role-mock-data';
import { RoleListPage } from '../pages/role-list-page';

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

function renderCreateRolePage(initialCreateOpen = false) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/roles']}>
        <RoleListPage initialCreateOpen={initialCreateOpen} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Create Role UI (M2-073 / Issue #235 / UC58)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    useAuthStore.getState().clearAuth();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    useAuthStore.getState().clearAuth();
  });

  it('opens Create Role modal from Thêm vai trò button, submits normalized payload, and adds new role to table and metrics', async () => {
    const createSpy = vi.spyOn(rolePermissionsApi, 'createRoleApi');
    renderCreateRolePage();

    fireEvent.click(screen.getByRole('button', { name: 'Thêm vai trò' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: 'Tạo vai trò mới' })).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText('Mã vai trò'), {
      target: { value: '  clinical_coordinator  ' },
    });
    fireEvent.change(within(dialog).getByLabelText('Tên vai trò'), {
      target: { value: '  Điều phối viên lâm sàng  ' },
    });
    fireEvent.change(within(dialog).getByLabelText('Nhóm phân hệ vai trò'), {
      target: { value: 'CLINICAL' },
    });
    fireEvent.change(within(dialog).getByLabelText('Mô tả vai trò'), {
      target: { value: 'Điều phối lịch khám và hàng đợi ngoại trú.' },
    });
    fireEvent.change(within(dialog).getByLabelText('Ghi chú phân tách nhiệm vụ (SoD)'), {
      target: { value: 'Không trực tiếp kê đơn hoặc cấp phát thuốc.' },
    });

    fireEvent.click(
      within(dialog).getByLabelText('Quyền PATIENT_PROFILE_READ - Xem hồ sơ bệnh nhân'),
    );
    fireEvent.click(within(dialog).getByLabelText('Quyền PATIENT_CHECKIN - Tiếp nhận & Check-in'));

    expect(within(dialog).getByText('Đã chọn: 2 / 14 quyền')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText('Ghi chú kiểm toán tạo vai trò'), {
      target: { value: 'Khởi tạo vai trò điều phối viên M2' },
    });

    fireEvent.click(within(dialog).getByRole('button', { name: /Tạo vai trò/i }));

    await waitFor(() => {
      expect(createSpy).toHaveBeenCalledWith({
        code: 'CLINICAL_COORDINATOR',
        name: 'Điều phối viên lâm sàng',
        scope: 'CLINICAL',
        status: 'ACTIVE',
        description: 'Điều phối lịch khám và hàng đợi ngoại trú.',
        segregationNote: 'Không trực tiếp kê đơn hoặc cấp phát thuốc.',
        permissionCodes: ['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN'],
        note: 'Khởi tạo vai trò điều phối viên M2',
      });
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(
      screen.getByText(
        'Đã tạo vai trò Điều phối viên lâm sàng (CLINICAL_COORDINATOR) thành công với 2 quyền hạn khởi tạo.',
      ),
    ).toBeInTheDocument();

    expect(screen.getByText('CLINICAL_COORDINATOR')).toBeInTheDocument();
    expect(screen.getByText('Điều phối viên lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('11')).toBeInTheDocument();
  });

  it('blocks submission on client validation errors (missing code/name, invalid code format, SoD conflict)', async () => {
    const createSpy = vi.spyOn(rolePermissionsApi, 'createRoleApi');
    renderCreateRolePage(true);

    const dialog = screen.getByRole('dialog');

    // 1. Empty form submission
    fireEvent.click(within(dialog).getByRole('button', { name: /Tạo vai trò/i }));
    expect(
      (await within(dialog).findAllByText('Mã vai trò không được để trống.')).length,
    ).toBeGreaterThanOrEqual(1);
    expect(within(dialog).getByText('Tên vai trò không được để trống.')).toBeInTheDocument();
    expect(createSpy).not.toHaveBeenCalled();

    // 2. Invalid code format + SoD conflict
    fireEvent.change(within(dialog).getByLabelText('Mã vai trò'), {
      target: { value: '123-BAD' },
    });
    fireEvent.change(within(dialog).getByLabelText('Tên vai trò'), {
      target: { value: 'Vai trò kiểm thử' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: /Tạo vai trò/i }));

    expect(
      (
        await within(dialog).findAllByText(
          'Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).',
        )
      ).length,
    ).toBeGreaterThanOrEqual(1);

    // 3. Fix code but select conflicting SoD permissions
    fireEvent.change(within(dialog).getByLabelText('Mã vai trò'), {
      target: { value: 'SOD_TEST_ROLE' },
    });
    fireEvent.click(
      within(dialog).getByLabelText('Quyền PRESCRIPTION_ISSUE - Kê đơn thuốc điện tử'),
    );
    fireEvent.click(
      within(dialog).getByLabelText('Quyền MEDICINE_DISPENSE - Cấp phát thuốc nhà thuốc'),
    );
    fireEvent.click(within(dialog).getByRole('button', { name: /Tạo vai trò/i }));

    expect(
      await within(dialog).findByText(
        'Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.',
      ),
    ).toBeInTheDocument();
    expect(createSpy).not.toHaveBeenCalled();
  });

  it('surfaces HTTP 409 Conflict ProblemDetails inside modal alert and keeps modal open', async () => {
    vi.spyOn(rolePermissionsApi, 'createRoleApi').mockRejectedValueOnce(
      makeAxiosError(409, "Mã vai trò 'DOCTOR' đã tồn tại trong hệ thống."),
    );

    renderCreateRolePage(true);

    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Mã vai trò'), {
      target: { value: 'DOCTOR' },
    });
    fireEvent.change(within(dialog).getByLabelText('Tên vai trò'), {
      target: { value: 'Bác sĩ bản sao' },
    });

    fireEvent.click(within(dialog).getByRole('button', { name: /Tạo vai trò/i }));

    expect(
      await within(dialog).findByText("Mã vai trò 'DOCTOR' đã tồn tại trong hệ thống."),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('enforces SYSTEM_ADMIN route access for /admin/roles/new and suppresses modal for HOSPITAL_ADMIN', () => {
    expect(canAccessRoute('/admin/roles/new', ['SYSTEM_ADMIN'])).toBe(true);
    expect(canAccessRoute('/admin/roles/new', ['HOSPITAL_ADMIN'])).toBe(false);
    expect(canAccessRoute('/admin/roles/new', ['DOCTOR'])).toBe(false);

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

    renderCreateRolePage(true);

    expect(screen.queryByRole('button', { name: 'Thêm vai trò' })).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
