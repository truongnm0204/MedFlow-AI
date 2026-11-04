import { Grid3X3, List, ShieldCheck } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button, toast } from '@/shared/ui';

import { RoleDetailModal } from '../components/role-detail-modal';
import { RoleFilterBar } from '../components/role-filters';
import { RoleMetricsCards } from '../components/role-metrics';
import { RolePermissionMatrixModal } from '../components/role-permission-matrix-modal';
import { RolePermissionMatrixView } from '../components/role-permission-matrix-view';
import { RoleTable } from '../components/role-table';
import {
  ROLE_METRICS_MOCK,
  ROLE_SCOPE_LABELS,
  ROLE_STATUS_LABELS,
  ROLES_MOCK_DATA,
} from '../data/role-mock-data';
import {
  useAssignRolePermissions,
  useRolesPermissionsCatalog,
} from '../hooks/use-role-permissions';
import type {
  RoleFilters,
  RoleItem,
  RoleViewMode,
  UpdateRolePermissionsRequest,
} from '../types/role.types';
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

export interface RoleListPageProps {
  initialView?: RoleViewMode;
}

/** SCR-ADM-ROLE-01 & SCR-ADM-ROLE-02 — RBAC system role list and permission assignment matrix. */
export function RoleListPage({ initialView = 'list' }: RoleListPageProps = {}) {
  const user = useAuthStore((state) => state.user);
  const canManageRoles = !user || user.roles.includes('SYSTEM_ADMIN');
  const isReadOnlyAdmin = Boolean(user) && !canManageRoles;

  const catalogQuery = useRolesPermissionsCatalog();
  const assignMutation = useAssignRolePermissions();

  const roles = catalogQuery.data?.roles ?? ROLES_MOCK_DATA;
  const metrics = catalogQuery.data?.metrics ?? ROLE_METRICS_MOCK;

  const [viewMode, setViewMode] = useState<RoleViewMode>(initialView);
  const [filters, setFilters] = useState<RoleFilters>({
    query: '',
    scope: '',
    status: '',
  });
  const [page, setPage] = useState(1);
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [assigningRoleId, setAssigningRoleId] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [pageErrorMessage, setPageErrorMessage] = useState<string | null>(null);
  const [modalErrorMessage, setModalErrorMessage] = useState<string | null>(null);

  const selectedRole = useMemo(
    () => roles.find((item) => item.id === selectedRoleId) ?? null,
    [roles, selectedRoleId],
  );
  const assigningRole = useMemo(
    () => roles.find((item) => item.id === assigningRoleId) ?? null,
    [roles, assigningRoleId],
  );

  const filtered = useMemo(() => {
    const q = filters.query.trim().toLowerCase();
    return roles.filter((item) => {
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
  }, [roles, filters]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  const updateFilters = (patch: Partial<RoleFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
    setPage(1);
  };

  const handleOpenAssignModal = (role: RoleItem) => {
    if (!canManageRoles) return;
    setModalErrorMessage(null);
    setAssigningRoleId(role.id);
  };

  const handleSavePermissions = async (payload: UpdateRolePermissionsRequest) => {
    setStatusMessage(null);
    setPageErrorMessage(null);
    setModalErrorMessage(null);
    try {
      const updated = await assignMutation.mutateAsync(payload);
      setStatusMessage(
        `Đã cập nhật phân quyền cho vai trò ${updated.name} (${updated.code}): ${updated.permissions.length} quyền hạn có hiệu lực.`,
      );
      toast.success(
        `Đã lưu ${updated.permissions.length} quyền hạn cho vai trò ${updated.code}.`,
        'Cập nhật phân quyền thành công',
      );
      setAssigningRoleId(null);
    } catch (err) {
      const msg = getErrorMessage(err, 'Không thể cập nhật phân quyền cho vai trò.');
      setModalErrorMessage(msg);
      setPageErrorMessage(msg);
      toast.error(msg, 'Lỗi phân quyền vai trò');
    }
  };

  const catalogErrorText = catalogQuery.isError
    ? getErrorMessage(catalogQuery.error, 'Không thể tải danh mục vai trò và quyền hạn.')
    : null;

  return (
    <AdminPortalShell
      activeTab="Vai trò & Phân quyền"
      breadcrumbs={[
        { label: 'Quản trị Hệ thống' },
        { label: 'Danh mục Vai trò & Phân quyền (SCR-ADM-ROLE-01 / SCR-ADM-ROLE-02)' },
      ]}
    >
      <div className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold text-navy">Danh mục Vai trò & Phân quyền</h1>
            <p className="mt-1 text-sm text-sub">
              Quản lý danh sách vai trò hệ thống (RBAC), phạm vi phân hệ và ma trận gán / thu hồi
              quyền hạn truy cập (SCR-ADM-ROLE-02).
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <div
              role="tablist"
              aria-label="Chế độ hiển thị vai trò"
              className="inline-flex rounded-xl border border-border bg-white p-1 shadow-2xs"
            >
              <button
                type="button"
                role="tab"
                aria-selected={viewMode === 'list'}
                onClick={() => setViewMode('list')}
                className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
                  viewMode === 'list'
                    ? 'bg-primary text-white shadow-2xs'
                    : 'text-sub hover:bg-canvas hover:text-navy'
                }`}
              >
                <List className="h-3.5 w-3.5" aria-hidden="true" />
                Danh sách vai trò
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={viewMode === 'matrix'}
                onClick={() => setViewMode('matrix')}
                className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
                  viewMode === 'matrix'
                    ? 'bg-primary text-white shadow-2xs'
                    : 'text-sub hover:bg-canvas hover:text-navy'
                }`}
              >
                <Grid3X3 className="h-3.5 w-3.5" aria-hidden="true" />
                Ma trận phân quyền
              </button>
            </div>

            {canManageRoles && (
              <Button
                type="button"
                size="sm"
                aria-label="Mở phân quyền nhanh"
                onClick={() => {
                  const target = filtered[0] ?? roles[0];
                  if (target) handleOpenAssignModal(target);
                }}
              >
                <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                Gán quyền theo vai trò
              </Button>
            )}
          </div>
        </div>

        {isReadOnlyAdmin && (
          <Alert tone="info">
            Chế độ chỉ đọc: Chỉ Quản trị viên hệ thống (SYSTEM_ADMIN) mới có quyền gán hoặc thu hồi
            quyền hạn của vai trò.
          </Alert>
        )}

        {statusMessage && (
          <div role="status">
            <Alert tone="success">{statusMessage}</Alert>
          </div>
        )}

        {(pageErrorMessage || catalogErrorText) && (
          <div role="alert">
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>{pageErrorMessage ?? catalogErrorText}</span>
                {catalogQuery.isError && (
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    onClick={() => void catalogQuery.refetch()}
                  >
                    Thử tải lại
                  </Button>
                )}
              </div>
            </Alert>
          </div>
        )}

        <RoleMetricsCards metrics={metrics} />
        <RoleFilterBar
          filters={filters}
          onChange={updateFilters}
          onExport={() => downloadRolesCsv(filtered)}
          onAdd={canManageRoles ? () => undefined : undefined}
        />

        {viewMode === 'list' ? (
          <RoleTable
            items={visible}
            total={filtered.length}
            page={currentPage}
            totalPages={totalPages}
            pageSize={PAGE_SIZE}
            onPageChange={setPage}
            onSelectRole={(role) => setSelectedRoleId(role.id)}
            onAssignPermissions={canManageRoles ? handleOpenAssignModal : undefined}
          />
        ) : (
          <RolePermissionMatrixView
            roles={filtered}
            canAssignPermissions={canManageRoles}
            onSelectRoleForAssign={handleOpenAssignModal}
          />
        )}
      </div>
      <RoleDetailModal
        role={selectedRole}
        onClose={() => setSelectedRoleId(null)}
        onAssignPermissions={
          canManageRoles
            ? (role) => {
                setSelectedRoleId(null);
                handleOpenAssignModal(role);
              }
            : undefined
        }
      />
      <RolePermissionMatrixModal
        role={assigningRole}
        isSubmitting={assignMutation.isPending}
        serverError={modalErrorMessage}
        onClose={() => {
          setModalErrorMessage(null);
          setAssigningRoleId(null);
        }}
        onSave={handleSavePermissions}
      />
    </AdminPortalShell>
  );
}
