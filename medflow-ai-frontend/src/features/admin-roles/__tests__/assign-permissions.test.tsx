import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

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

function renderRolePermissionsPage(initialView: 'list' | 'matrix' = 'list') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/roles']}>
        <RoleListPage initialView={initialView} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Assign Permissions (SCR-ADM-ROLE-02)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    useAuthStore.getState().clearAuth();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    useAuthStore.getState().clearAuth();
  });

  it('assigns and revokes permissions for a role and updates table and status banner', async () => {
    const assignSpy = vi.spyOn(rolePermissionsApi, 'assignRolePermissionsApi');
    renderRolePermissionsPage();

    fireEvent.click(screen.getByRole('button', { name: 'Phân quyền Bác sĩ lâm sàng' }));

    const dialog = screen.getByRole('dialog');
    expect(
      within(dialog).getByText('Ma trận gán / thu hồi quyền hạn truy cập (SCR-ADM-ROLE-02)'),
    ).toBeInTheDocument();

    const queueCheckbox = within(dialog).getByLabelText(
      'Quyền QUEUE_MANAGE - Quản lý hàng đợi khám',
    ) as HTMLInputElement;
    const paymentCheckbox = within(dialog).getByLabelText(
      'Quyền PAYMENT_VIEW - Tra cứu viện phí & thanh toán',
    ) as HTMLInputElement;

    expect(queueCheckbox.checked).toBe(false);
    expect(paymentCheckbox.checked).toBe(true);

    fireEvent.click(queueCheckbox);
    fireEvent.click(paymentCheckbox);

    expect(within(dialog).getByText('+1 cấp mới')).toBeInTheDocument();
    expect(within(dialog).getByText('-1 thu hồi')).toBeInTheDocument();

    const noteInput = within(dialog).getByLabelText('Ghi chú kiểm toán phân quyền');
    fireEvent.change(noteInput, { target: { value: 'Cập nhật quyền điều phối hàng đợi' } });

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu phân quyền/i }));

    await waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith({
        roleId: 'ROL-03',
        roleCode: 'DOCTOR',
        permissionCodes: [
          'PATIENT_PROFILE_READ',
          'QUEUE_MANAGE',
          'MEDICAL_RECORD_WRITE',
          'DIAGNOSTIC_ORDER',
          'PRESCRIPTION_ISSUE',
        ],
        note: 'Cập nhật quyền điều phối hàng đợi',
      });
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    expect(
      screen.getByText(
        'Đã cập nhật phân quyền cho vai trò Bác sĩ lâm sàng (DOCTOR): 5 quyền hạn có hiệu lực.',
      ),
    ).toBeInTheDocument();

    const doctorRow = screen.getByText('Bác sĩ lâm sàng').closest('tr');
    expect(doctorRow).not.toBeNull();
    if (doctorRow) {
      expect(within(doctorRow).getByText('5 quyền')).toBeInTheDocument();
      expect(within(doctorRow).getByText('QUEUE_MANAGE')).toBeInTheDocument();
    }
  });

  it('blocks submission on client validation errors (SoD violation and SYSTEM_ADMIN self-lockout)', async () => {
    const assignSpy = vi.spyOn(rolePermissionsApi, 'assignRolePermissionsApi');
    renderRolePermissionsPage();

    // 1. Segregation of Duties (SoD) check on DOCTOR
    fireEvent.click(screen.getByRole('button', { name: 'Phân quyền Bác sĩ lâm sàng' }));
    let dialog = screen.getByRole('dialog');

    const dispenseCheckbox = within(dialog).getByLabelText(
      'Quyền MEDICINE_DISPENSE - Cấp phát thuốc nhà thuốc',
    );
    fireEvent.click(dispenseCheckbox);
    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu phân quyền/i }));

    expect(
      await within(dialog).findByText(
        'Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.',
      ),
    ).toBeInTheDocument();
    expect(assignSpy).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Hủy' }));

    // 2. Self-lockout check on SYSTEM_ADMIN
    fireEvent.click(screen.getByRole('button', { name: 'Phân quyền Quản trị hệ thống' }));
    dialog = screen.getByRole('dialog');

    const userManageCheckbox = within(dialog).getByLabelText(
      'Quyền USER_MANAGE - Quản lý tài khoản & vai trò',
    );
    fireEvent.click(userManageCheckbox);
    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu phân quyền/i }));

    expect(
      await within(dialog).findByText(
        'Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.',
      ),
    ).toBeInTheDocument();
    expect(assignSpy).not.toHaveBeenCalled();
  });

  it('hides permission assignment and role creation buttons for non-SYSTEM_ADMIN users (HOSPITAL_ADMIN)', () => {
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

    renderRolePermissionsPage();

    expect(screen.queryByRole('button', { name: /Phân quyền/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Thêm vai trò/i })).toBeNull();
    expect(
      screen.getByText(
        /Chế độ chỉ đọc: Chỉ Quản trị viên hệ thống \(SYSTEM_ADMIN\) mới có quyền gán hoặc thu hồi quyền hạn của vai trò\./i,
      ),
    ).toBeInTheDocument();
  });

  it('displays server ProblemDetails error inside alert and keeps modal open when API rejects with 403', async () => {
    vi.spyOn(rolePermissionsApi, 'assignRolePermissionsApi').mockRejectedValueOnce(
      makeAxiosError(403, 'Chỉ SYSTEM_ADMIN mới được phép thay đổi ma trận phân quyền.'),
    );

    renderRolePermissionsPage();

    fireEvent.click(screen.getByRole('button', { name: 'Phân quyền Bác sĩ lâm sàng' }));
    const dialog = screen.getByRole('dialog');

    fireEvent.click(within(dialog).getByRole('button', { name: /Lưu phân quyền/i }));

    expect(
      await within(dialog).findByText(
        'Chỉ SYSTEM_ADMIN mới được phép thay đổi ma trận phân quyền.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('switches to Cross-Role Matrix View and opens assignment modal from matrix column header', () => {
    renderRolePermissionsPage();

    fireEvent.click(screen.getByRole('tab', { name: 'Ma trận phân quyền' }));

    const matrixTable = screen.getByRole('table', { name: 'Bảng ma trận phân quyền vai trò' });
    expect(matrixTable).toBeInTheDocument();
    expect(within(matrixTable).getByText('PATIENT_PROFILE_READ')).toBeInTheDocument();
    expect(within(matrixTable).getByText('PAYMENT_VIEW')).toBeInTheDocument();

    fireEvent.click(
      within(matrixTable).getByRole('button', { name: 'Phân quyền Bác sĩ lâm sàng' }),
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Tìm kiếm quyền hạn trong ma trận')).toBeInTheDocument();
  });
});
