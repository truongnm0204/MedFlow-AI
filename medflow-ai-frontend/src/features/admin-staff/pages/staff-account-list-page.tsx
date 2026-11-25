import { Loader2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Alert, Button } from '@/shared/ui';

import { DeactivateStaffModal } from '../components/deactivate-staff-modal';
import { StaffAccountFilterBar } from '../components/staff-account-filters';
import { StaffAccountMetricsCards } from '../components/staff-account-metrics';
import { StaffAccountTable } from '../components/staff-account-table';
import { STAFF_STATUS_LABELS } from '../data/staff-account-mock-data';
import {
  useAdminStaffAccounts,
  useDeactivateStaffAccountMutation,
} from '../hooks/use-staff-accounts';
import type { DeactivateStaffPayload, StaffTargetSummary } from '../types/deactivate-staff.types';
import type {
  StaffAccountFilters,
  StaffAccountItem,
  StaffAccountMetrics,
} from '../types/staff-account.types';

const PAGE_SIZE = 8;

const EMPTY_STAFF_ACCOUNTS: StaffAccountItem[] = [];

const EMPTY_METRICS: StaffAccountMetrics = {
  total: 0,
  active: 0,
  locked: 0,
  disabled: 0,
};

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

/** SCR-ADM-STAFF-01 — staff account list backed by GET /api/v1/admin/staff. */
export function StaffAccountListPage() {
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const canManageStaff = !user || user.roles.includes('SYSTEM_ADMIN');
  const isReadOnlyAdmin = Boolean(user) && !canManageStaff;

  const { data, isLoading, isError, error, refetch } = useAdminStaffAccounts();
  const deactivateMutation = useDeactivateStaffAccountMutation();

  const accounts = data?.items ?? EMPTY_STAFF_ACCOUNTS;
  const metrics = data?.metrics ?? EMPTY_METRICS;

  const [filters, setFilters] = useState<StaffAccountFilters>({
    query: '',
    department: '',
    role: '',
    status: '',
  });
  const [page, setPage] = useState(1);
  const [target, setTarget] = useState<StaffTargetSummary | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

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
    const meta = item as StaffAccountItem & { staffId?: number; pendingAssignments?: number };
    setTarget({
      id: meta.staffId ? String(meta.staffId) : item.id,
      staffCode: item.code,
      fullName: item.fullName,
      role: item.role,
      department: item.department,
      email: item.email,
      pendingAssignments: meta.pendingAssignments ?? 0,
    });
  };

  const handleConfirmDeactivate = async (payload: DeactivateStaffPayload) => {
    setActionError(null);
    try {
      await deactivateMutation.mutateAsync(payload);
      setTarget(null);
    } catch (err) {
      setActionError(
        err instanceof Error ? err.message : 'Không thể vô hiệu hóa tài khoản nhân sự.',
      );
      setTarget(null);
    }
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
        {isError ? (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                <b>Không thể tải danh sách tài khoản nhân sự:</b>{' '}
                {error instanceof Error ? error.message : 'Đã xảy ra lỗi kết nối máy chủ.'}
              </span>
              <Button type="button" size="sm" variant="secondary" onClick={() => void refetch()}>
                Thử lại
              </Button>
            </div>
          </Alert>
        ) : null}
        {actionError ? <Alert tone="error">{actionError}</Alert> : null}
        <StaffAccountMetricsCards metrics={metrics} />
        <StaffAccountFilterBar
          filters={filters}
          onChange={updateFilters}
          onExport={() => downloadCsv(filtered)}
          onAdd={canManageStaff ? () => navigate('/admin/staff/new') : undefined}
        />
        {isLoading ? (
          <div className="flex items-center justify-center gap-2 rounded-xl border border-border bg-white py-12 text-sm text-sub">
            <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
            <span>Đang tải danh sách tài khoản nhân sự...</span>
          </div>
        ) : (
          <StaffAccountTable
            items={visible}
            total={filtered.length}
            page={currentPage}
            totalPages={totalPages}
            pageSize={PAGE_SIZE}
            onPageChange={setPage}
            onView={(item) => navigate(`/admin/staff/${item.code}/edit`)}
            onDeactivate={canManageStaff ? handleDeactivate : undefined}
          />
        )}
      </div>
      {target && (
        <DeactivateStaffModal
          staff={target}
          onCancel={() => setTarget(null)}
          onConfirm={(payload) => void handleConfirmDeactivate(payload)}
        />
      )}
    </AdminPortalShell>
  );
}
