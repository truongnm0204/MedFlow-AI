import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchRoleByIdApi, fetchRoleListApi } from '../api/role.api';
import { RoleListPage } from '../pages/role-list-page';
import type { RoleListResponseDto } from '../types/role.types';

const renderPage = () =>
  render(
    <MemoryRouter>
      <RoleListPage />
    </MemoryRouter>,
  );

describe('RoleListPage (SCR-ADM-ROLE-01)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders metric cards and table columns', () => {
    renderPage();
    expect(screen.getByText('TỔNG SỐ VAI TRÒ')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TÀI KHOẢN ĐƯỢC GÁN')).toBeInTheDocument();
    expect(screen.getByText('QUYỀN HẠT NHÂN')).toBeInTheDocument();
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('09')).toBeInTheDocument();
    expect(screen.getByText('14')).toBeInTheDocument();

    for (const column of [
      'MÃ VAI TRÒ',
      'TÊN VAI TRÒ / MÔ TẢ',
      'NHÓM PHÂN HỆ',
      'QUYỀN HẠN',
      'TÀI KHOẢN',
      'TRẠNG THÁI',
      'THAO TÁC',
    ]) {
      expect(screen.getByRole('columnheader', { name: column })).toBeInTheDocument();
    }

    expect(screen.getByText('SYSTEM_ADMIN')).toBeInTheDocument();
    expect(screen.getByText('DOCTOR')).toBeInTheDocument();
  });

  it('filters roles by search query', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm kiếm vai trò'), {
      target: { value: 'PHARMACY_STAFF' },
    });
    expect(screen.getByText('Nhân viên nhà thuốc')).toBeInTheDocument();
    expect(screen.queryByText('Bác sĩ lâm sàng')).not.toBeInTheDocument();
  });

  it('filters by scope and status and shows empty state', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Lọc theo nhóm vai trò'), {
      target: { value: 'SYSTEM' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'INACTIVE' },
    });
    expect(screen.getByText('SECURITY_AUDITOR')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Lọc theo nhóm vai trò'), {
      target: { value: 'PORTAL' },
    });
    expect(screen.getByText('Không tìm thấy vai trò hệ thống phù hợp.')).toBeInTheDocument();
  });

  it('paginates the role list', () => {
    renderPage();
    expect(screen.getByText('Trang 1 / 2')).toBeInTheDocument();
    expect(screen.queryByText('SECURITY_AUDITOR')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('SECURITY_AUDITOR')).toBeInTheDocument();
  });

  it('opens and closes role permission detail modal', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Xem chi tiết Bác sĩ lâm sàng' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getAllByText('MEDICAL_RECORD_WRITE').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Kê đơn thuốc điện tử')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Đóng'));
    expect(screen.queryByText('Kê đơn thuốc điện tử')).not.toBeInTheDocument();
  });

  it('fetches and renders roles from GET /api/v1/admin/roles when backend responds', async () => {
    const backendResponse: RoleListResponseDto = {
      data: [
        {
          roleId: 1,
          id: 'ROL-01',
          code: 'SYSTEM_ADMIN',
          name: 'Quản trị hệ thống (Backend)',
          description: 'Vai trò từ API /api/v1/admin/roles',
          scope: 'SYSTEM',
          isSystemRole: true,
          assignedUsersCount: 5,
          activeUsersCount: 5,
          permissionCount: 2,
          permissions: [
            {
              permissionId: 12,
              code: 'USER_MANAGE',
              name: 'Quản lý tài khoản & vai trò',
              module: 'Quản trị hệ thống',
            },
            {
              permissionId: 13,
              code: 'AUDIT_READ',
              name: 'Tra cứu nhật ký kiểm toán',
              module: 'An ninh & Kiểm toán',
            },
          ],
          status: 'ACTIVE',
          updatedAt: '20/04/2026',
          segregationNote: 'Tách biệt SoD.',
        },
      ],
      items: [],
      total: 1,
      page: 1,
      pageSize: 100,
      totalPages: 1,
      metrics: {
        totalRoles: 7,
        activeRoles: 7,
        inactiveRoles: 0,
        systemRoles: 7,
        customRoles: 0,
        totalAssignedAccounts: 25,
        activeAssignedAccounts: 25,
        totalPermissions: 14,
      },
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: backendResponse });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Quản trị hệ thống (Backend)')).toBeInTheDocument();
    });

    expect(getSpy).toHaveBeenCalledWith('/admin/roles', {
      params: { page: 1, pageSize: 100 },
    });
    expect(screen.getByText('25')).toBeInTheDocument();
  });

  it('maps query params in fetchRoleListApi and fetchRoleByIdApi', async () => {
    const backendResponse: RoleListResponseDto = {
      data: [
        {
          roleId: 3,
          id: 'ROL-03',
          code: 'DOCTOR',
          name: 'Bác sĩ lâm sàng',
          description: 'Thực hiện khám bệnh',
          scope: 'CLINICAL',
          isSystemRole: true,
          assignedUsersCount: 12,
          activeUsersCount: 11,
          permissionCount: 1,
          permissions: [
            {
              permissionId: 6,
              code: 'MEDICAL_RECORD_WRITE',
              name: 'Ghi nhận bệnh án lâm sàng',
              module: 'Khám lâm sàng',
            },
          ],
          status: 'ACTIVE',
          updatedAt: '12/03/2026',
          segregationNote: 'Chỉ truy cập lượt khám phân công.',
        },
      ],
      total: 1,
      page: 1,
      pageSize: 10,
      totalPages: 1,
      metrics: {
        totalRoles: 7,
        activeRoles: 7,
        inactiveRoles: 0,
        systemRoles: 7,
        customRoles: 0,
        totalAssignedAccounts: 12,
        activeAssignedAccounts: 11,
        totalPermissions: 14,
      },
    };

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: backendResponse })
      .mockResolvedValueOnce({ data: backendResponse.data[0] });

    const listResult = await fetchRoleListApi({
      query: 'DOCTOR',
      scope: 'CLINICAL',
      status: 'ACTIVE',
      isSystemRole: true,
      page: 1,
      pageSize: 10,
    });

    expect(getSpy).toHaveBeenCalledWith('/admin/roles', {
      params: {
        query: 'DOCTOR',
        scope: 'CLINICAL',
        status: 'ACTIVE',
        isSystemRole: true,
        page: 1,
        pageSize: 10,
      },
    });
    expect(listResult.items).toHaveLength(1);
    expect(listResult.items[0].code).toBe('DOCTOR');

    const singleRole = await fetchRoleByIdApi(3);
    expect(getSpy).toHaveBeenCalledWith('/admin/roles/3');
    expect(singleRole.id).toBe('ROL-03');
  });
});
