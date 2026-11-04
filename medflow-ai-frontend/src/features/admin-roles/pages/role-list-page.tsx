import { useMemo, useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { RoleDetailModal } from '../components/role-detail-modal';
import { RoleFilterBar } from '../components/role-filters';
import { RoleMetricsCards } from '../components/role-metrics';
import { RoleTable } from '../components/role-table';
import {
  ROLE_METRICS_MOCK,
  ROLE_SCOPE_LABELS,
  ROLE_STATUS_LABELS,
  ROLES_MOCK_DATA,
} from '../data/role-mock-data';
import type { RoleFilters, RoleItem } from '../types/role.types';

const PAGE_SIZE = 8;

function downloadRolesCsv(items: RoleItem[]) {
  const rows = [
    [
      'Mã vai trò',
      'Tên vai trò',
      'Nhóm phân hệ',
      'Loại vai trò',
      'Số quyền hạn',
      'Tài khoản gán',
      'Trạng thái',
    ],
    ...items.map((item) => [
      item.code,
      item.name,
      ROLE_SCOPE_LABELS[item.scope],
      item.isSystemRole ? 'Hệ thống' : 'Tùy chỉnh',
      String(item.permissions.length),
      String(item.assignedUsersCount),
      ROLE_STATUS_LABELS[item.status],
    ]),
  ];
  const csv =
    '\uFEFF' +
    rows
      .map((row) =>
        row.map((value) => `"${value.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`).join(','),
      )
      .join('\r\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-sach-vai-tro-he-thong.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-ROLE-01 — RBAC system role list and permission inspection. */
export function RoleListPage() {
  const user = useAuthStore((state) => state.user);
  const canCreateRole = !user || user.roles.includes('SYSTEM_ADMIN');

  const [filters, setFilters] = useState<RoleFilters>({
    query: '',
    scope: '',
    status: '',
  });
  const [page, setPage] = useState(1);
  const [selectedRole, setSelectedRole] = useState<RoleItem | null>(null);

  const filtered = useMemo(() => {
    const q = filters.query.trim().toLowerCase();
    return ROLES_MOCK_DATA.filter((item) => {
      const matchesQuery =
        !q ||
        item.code.toLowerCase().includes(q) ||
        item.name.toLowerCase().includes(q) ||
        item.description.toLowerCase().includes(q) ||
        item.permissions.some(
          (perm) => perm.code.toLowerCase().includes(q) || perm.name.toLowerCase().includes(q),
        );
      return (
        matchesQuery &&
        (!filters.scope || item.scope === filters.scope) &&
        (!filters.status || item.status === filters.status)
      );
    });
  }, [filters]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  const updateFilters = (patch: Partial<RoleFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
    setPage(1);
  };

  return (
    <AdminPortalShell
      activeTab="Vai trò & Phân quyền"
      breadcrumbs={[
        { label: 'Quản trị Hệ thống' },
        { label: 'Danh mục Vai trò & Phân quyền (SCR-ADM-ROLE-01)' },
      ]}
    >
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-navy">Danh mục Vai trò & Phân quyền</h1>
          <p className="mt-1 text-sm text-sub">
            Quản lý danh sách vai trò hệ thống (RBAC), phạm vi phân hệ và quyền hạn truy cập.
          </p>
        </div>
        <RoleMetricsCards metrics={ROLE_METRICS_MOCK} />
        <RoleFilterBar
          filters={filters}
          onChange={updateFilters}
          onExport={() => downloadRolesCsv(filtered)}
          onAdd={canCreateRole ? () => undefined : undefined}
        />
        <RoleTable
          items={visible}
          total={filtered.length}
          page={currentPage}
          totalPages={totalPages}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          onSelectRole={setSelectedRole}
        />
      </div>
      <RoleDetailModal role={selectedRole} onClose={() => setSelectedRole(null)} />
    </AdminPortalShell>
  );
}
