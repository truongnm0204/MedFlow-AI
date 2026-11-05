import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Alert } from '@/shared/ui';
import { DeactivateStaffModal } from '../components/deactivate-staff-modal';
import { StaffAccountFilterBar } from '../components/staff-account-filters';
import { StaffAccountMetricsCards } from '../components/staff-account-metrics';
import { StaffAccountTable } from '../components/staff-account-table';
import {
  STAFF_ACCOUNT_METRICS,
  STAFF_ACCOUNTS,
  STAFF_STATUS_LABELS,
} from '../data/staff-account-mock-data';
import type { DeactivateStaffPayload, StaffTargetSummary } from '../types/deactivate-staff.types';
import type {
  StaffAccountFilters,
  StaffAccountItem,
  StaffAccountMetrics,
} from '../types/staff-account.types';

const PAGE_SIZE = 8;

function downloadCsv(items: StaffAccountItem[]) {
  const quote = (value: string) => `"${value.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const rows = [
    ['Mã NV', 'Họ tên', 'Email', 'Vai trò', 'Khoa công tác', 'Cơ sở', 'Trạng thái'],
    ...items.map((item) => [
      item.code,
      item.fullName,
      item.email,
      item.role,
      item.department,
      item.facility,
      STAFF_STATUS_LABELS[item.status],
    ]),
  ];
  const csv = '\uFEFF' + rows.map((row) => row.map(quote).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-sach-tai-khoan-nhan-su.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-STAFF-01 — staff account list (synthetic data). */
export function StaffAccountListPage() {
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const canManageStaff = !user || user.roles.includes('SYSTEM_ADMIN');
  const isReadOnlyAdmin = Boolean(user) && !canManageStaff;
  const [accounts, setAccounts] = useState<StaffAccountItem[]>(STAFF_ACCOUNTS);
  const [metrics, setMetrics] = useState<StaffAccountMetrics>(STAFF_ACCOUNT_METRICS);
  const [filters, setFilters] = useState<StaffAccountFilters>({
    query: '',
    department: '',
    role: '',
    status: '',
  });
  const [page, setPage] = useState(1);
  const [target, setTarget] = useState<StaffTargetSummary | null>(null);

  const filtered = useMemo(() => {
    const q = filters.query.trim().toLowerCase();
    return accounts.filter(
      (item) =>
        (!q ||
          item.code.toLowerCase().includes(q) ||
          item.fullName.toLowerCase().includes(q) ||
          item.email.toLowerCase().includes(q)) &&
        (!filters.department || item.department === filters.department) &&
        (!filters.role || item.role === filters.role) &&
        (!filters.status || item.status === filters.status),
    );
  }, [accounts, filters]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  const updateFilters = (patch: Partial<StaffAccountFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
    setPage(1);
  };

  const handleDeactivate = (item: StaffAccountItem) => {
    setTarget({
      id: item.id,
      staffCode: item.code,
      fullName: item.fullName,
      role: item.role,
      department: item.department,
      email: item.email,
      pendingAssignments:
        (item as StaffAccountItem & { pendingAssignments?: number }).pendingAssignments ?? 0,
    });
  };

  const handleConfirmDeactivate = ({ staffId }: DeactivateStaffPayload) => {
    setAccounts((prev) =>
      prev.map((acc) => (acc.id === staffId ? { ...acc, status: 'DISABLED' } : acc)),
    );
    setMetrics((prev) => ({
      ...prev,
      active: Math.max(0, prev.active - 1),
      disabled: prev.disabled + 1,
    }));
    setTarget(null);
  };

  return (
    <AdminPortalShell
      activeTab="Bác sĩ & Nhân sự Y tế"
      breadcrumbs={[{ label: 'Cấu hình Cơ sở' }, { label: 'Tài khoản nhân sự' }]}
    >
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-navy">Tài khoản nhân sự</h1>
          <p className="mt-1 text-sm text-sub">Quản lý tài khoản đăng nhập của nhân sự y tế.</p>
        </div>
        {isReadOnlyAdmin && (
          <Alert tone="info">
            Chế độ chỉ đọc: Chỉ Quản trị viên hệ thống (SYSTEM_ADMIN) mới có quyền tạo mới, cập nhật
            hoặc vô hiệu hóa tài khoản nhân sự.
          </Alert>
        )}
        <StaffAccountMetricsCards metrics={metrics} />
        <StaffAccountFilterBar
          filters={filters}
          onChange={updateFilters}
          onExport={() => downloadCsv(filtered)}
          onAdd={canManageStaff ? () => navigate('/admin/staff/new') : undefined}
        />
        <StaffAccountTable
          items={visible}
          total={filtered.length}
          page={currentPage}
          totalPages={totalPages}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          onDeactivate={canManageStaff ? handleDeactivate : undefined}
        />
      </div>
      {target && (
        <DeactivateStaffModal
          staff={target}
          onCancel={() => setTarget(null)}
          onConfirm={handleConfirmDeactivate}
        />
      )}
    </AdminPortalShell>
  );
}
