import {
  ArrowUpRight,
  FileSearch,
  Filter,
  LogOut,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldCheck,
  UserCog,
} from 'lucide-react';
import { type FormEvent, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { SystemAdminAllowedModules } from '../components/system-admin-allowed-modules';
import { SystemAdminAuditLogTable } from '../components/system-admin-audit-log-table';
import { SystemAdminRoleSummaryCard } from '../components/system-admin-role-summary-card';
import { SystemAdminStatCards } from '../components/system-admin-stat-cards';
import { SYSTEM_ADMIN_HOSPITAL_OPTIONS, SYSTEM_ADMIN_META } from '../data/system-admin-mock-data';
import { useSystemAdminDashboard } from '../hooks/use-system-admin-dashboard';
import {
  classifyAuditAction,
  validateSystemAdminFilterInputs,
} from '../schemas/system-admin.schema';
import type {
  AuditActionCategory,
  GetSystemAdminDashboardParams,
  SystemAdminAllowedModuleDto,
  SystemAdminAuditLogItemDto,
} from '../types/system-admin.types';

type AuditCategoryFilter = 'ALL' | AuditActionCategory;

const EMPTY_LOGS: SystemAdminAuditLogItemDto[] = [];
const EMPTY_MODULES: SystemAdminAllowedModuleDto[] = [];

const AUDIT_CATEGORY_FILTER_OPTIONS: { id: AuditCategoryFilter; label: string }[] = [
  { id: 'ALL', label: 'Tất cả' },
  { id: 'AUTH', label: 'Xác thực (AUTH)' },
  { id: 'RBAC', label: 'Phân quyền (RBAC)' },
  { id: 'CONFIG', label: 'Cấu hình (CONFIG)' },
];

export function SystemAdminDashboardPage() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [queryParams, setQueryParams] = useState<GetSystemAdminDashboardParams>({
    recentAuditLimit: 10,
    hospitalId: null,
  });
  const [auditLimitInput, setAuditLimitInput] = useState('10');
  const [hospitalIdInput, setHospitalIdInput] = useState('');
  const [filterValidationError, setFilterValidationError] = useState<string | null>(null);

  const [searchQuery, setSearchQuery] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<AuditCategoryFilter>('ALL');
  const [selectedModuleOverride, setSelectedModuleOverride] =
    useState<SystemAdminAllowedModuleDto | null>(null);
  const [selectedLogOverride, setSelectedLogOverride] = useState<SystemAdminAuditLogItemDto | null>(
    null,
  );

  const { data, isPending, isError, error, refetch } = useSystemAdminDashboard(queryParams, true);

  const adminDisplayName =
    user?.fullName?.trim() ||
    data?.adminProfile.fullName?.trim() ||
    SYSTEM_ADMIN_META.defaultAdminName;
  const staffCode =
    user?.staffCode?.trim() ||
    data?.adminProfile.staffCode?.trim() ||
    SYSTEM_ADMIN_META.defaultStaffCode;
  const hospitalName = data?.adminProfile.hospitalName || SYSTEM_ADMIN_META.defaultHospitalName;
  const activePermissions = data?.adminProfile.permissions ?? [];

  const allowedModules = data?.allowedModules ?? EMPTY_MODULES;
  const auditLogs = data?.recentAuditLogs ?? EMPTY_LOGS;

  const filteredAuditLogs = useMemo(() => {
    const keyword = searchQuery.trim().toLowerCase();
    return auditLogs.filter((log) => {
      const cat = classifyAuditAction(log.action);
      const matchesCategory = categoryFilter === 'ALL' || cat === categoryFilter;
      const haystack =
        `AUD-${String(log.id).padStart(4, '0')} ${log.action} ${log.entityType} ${log.entityId ?? ''} ${log.reason ?? ''} ${log.actorEmail ?? ''} ${log.ipAddress ?? ''}`.toLowerCase();
      return matchesCategory && (!keyword || haystack.includes(keyword));
    });
  }, [auditLogs, categoryFilter, searchQuery]);

  const selectedModule = selectedModuleOverride ?? allowedModules[0] ?? null;
  const selectedLog = selectedLogOverride ?? filteredAuditLogs[0] ?? null;

  const handleApplyQueryFilter = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const validation = validateSystemAdminFilterInputs(auditLimitInput, hospitalIdInput);
    if (!validation.isValid || !validation.params) {
      setFilterValidationError(
        validation.errorMessage ?? 'Tham số lọc bảng điều khiển không hợp lệ.',
      );
      return;
    }

    setFilterValidationError(null);
    setQueryParams(validation.params);
  };

  const handleResetQueryFilter = () => {
    setAuditLimitInput('10');
    setHospitalIdInput('');
    setFilterValidationError(null);
    setQueryParams({ recentAuditLimit: 10, hospitalId: null });
  };

  const handleResetAuditTableFilters = () => {
    setSearchQuery('');
    setCategoryFilter('ALL');
  };

  return (
    <AdminPortalShell
      activeTab="Tổng quan hệ thống"
      breadcrumbText="Tổng quan quản trị hệ thống"
      breadcrumbLink="/system-admin"
    >
      <main className="mx-auto max-w-[1680px] space-y-6 p-6">
        {/* Role Landing Header Card */}
        <section
          aria-label="Thông tin phiên làm việc Quản trị Hệ thống"
          className="flex flex-col justify-between gap-4 rounded-2xl border border-border bg-white p-5 shadow-2xs lg:flex-row lg:items-center"
        >
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="sky">
                <ShieldCheck aria-hidden="true" className="mr-1 h-3.5 w-3.5" />
                Phân quyền: Quản trị hệ thống (UC57–UC60)
              </Badge>
              <Badge tone="emerald">{hospitalName}</Badge>
              <span className="rounded-md border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-ink">
                Mã NV: {staffCode}
              </span>
            </div>
            <h1 className="text-xl font-bold tracking-tight text-ink sm:text-2xl">
              Tổng quan quản trị hệ thống
            </h1>
            <p className="text-xs text-sub">
              Xin chào, <strong className="font-semibold text-ink">{adminDisplayName}</strong> •
              Quyền hiệu lực:{' '}
              <span className="font-mono font-semibold text-primary">
                {activePermissions.length > 0
                  ? activePermissions.join(', ')
                  : 'USER_MANAGE, HOSPITAL_CONFIG, AUDIT_READ'}
              </span>
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <Link
              to="/admin/staff"
              className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark"
            >
              <UserCog aria-hidden="true" className="h-4 w-4" />
              Quản lý Tài khoản nhân sự
            </Link>
            <Link
              to="/admin/roles"
              className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-ink transition-colors hover:bg-slate-50"
            >
              <ShieldCheck aria-hidden="true" className="h-4 w-4 text-primary" />
              Vai trò &amp; Phân quyền
            </Link>
            <button
              type="button"
              onClick={() => logout.mutate()}
              className="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-2 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-100 focus-visible:ring-2 focus-visible:ring-rose-500 focus-visible:outline-none"
            >
              <LogOut aria-hidden="true" className="h-3.5 w-3.5" />
              Đăng xuất
            </button>
          </div>
        </section>

        {/* Query Parameter Filter Form */}
        <section
          aria-label="Bộ lọc truy vấn bảng điều khiển hệ thống"
          className="rounded-2xl border border-border bg-white p-4 shadow-2xs"
        >
          <form
            onSubmit={handleApplyQueryFilter}
            noValidate
            className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between"
          >
            <div className="flex flex-wrap items-end gap-3">
              <div className="space-y-1">
                <label
                  htmlFor="system-admin-hospital-filter"
                  className="block text-[11px] font-semibold text-sub"
                >
                  Phạm vi cơ sở y tế
                </label>
                <select
                  id="system-admin-hospital-filter"
                  aria-label="Phạm vi cơ sở y tế"
                  value={hospitalIdInput}
                  onChange={(e) => setHospitalIdInput(e.target.value)}
                  className="h-9 rounded-lg border border-border bg-canvas px-3 text-xs font-medium text-ink focus:border-primary focus:bg-white focus:outline-none"
                >
                  <option value="">Toàn hệ thống MedFlow (Tất cả cơ sở)</option>
                  {SYSTEM_ADMIN_HOSPITAL_OPTIONS.map((h) => (
                    <option key={h.id} value={String(h.id)}>
                      {h.name}
                    </option>
                  ))}
                </select>
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="system-admin-audit-limit"
                  className="block text-[11px] font-semibold text-sub"
                >
                  Số lượng nhật ký hiển thị (1–50)
                </label>
                <input
                  id="system-admin-audit-limit"
                  aria-label="Số lượng nhật ký hiển thị"
                  type="number"
                  min={1}
                  max={50}
                  value={auditLimitInput}
                  onChange={(e) => setAuditLimitInput(e.target.value)}
                  className="h-9 w-40 rounded-lg border border-border bg-canvas px-3 font-mono text-xs font-semibold text-ink focus:border-primary focus:bg-white focus:outline-none"
                />
              </div>

              <div className="flex items-center gap-2">
                <button
                  type="submit"
                  className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-primary px-3.5 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <Filter aria-hidden="true" className="h-3.5 w-3.5" />
                  Áp dụng bộ lọc
                </button>
                <button
                  type="button"
                  onClick={handleResetQueryFilter}
                  className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-sub transition-colors hover:bg-slate-50 hover:text-ink"
                >
                  <RotateCcw aria-hidden="true" className="h-3.5 w-3.5" />
                  Đặt lại tham số
                </button>
              </div>
            </div>

            <div className="text-[11px] text-sub">
              Đang truy vấn:{' '}
              <span className="font-mono font-semibold text-ink">
                recentAuditLimit={queryParams.recentAuditLimit ?? 10}
              </span>{' '}
              •{' '}
              <span className="font-mono font-semibold text-ink">
                hospitalId={queryParams.hospitalId ?? 'ALL'}
              </span>
            </div>
          </form>

          {filterValidationError && (
            <div className="mt-3">
              <Alert tone="error">{filterValidationError}</Alert>
            </div>
          )}
        </section>

        {isPending && (
          <div
            role="status"
            className="rounded-2xl border border-border bg-white p-6 text-center text-xs font-medium text-sub"
          >
            Đang tải dữ liệu tổng quan quản trị hệ thống...
          </div>
        )}

        {isError && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                {getErrorMessage(
                  error,
                  'Không thể tải dữ liệu tổng quan quản trị hệ thống. Vui lòng thử lại.',
                )}
              </span>
              <button
                type="button"
                onClick={() => void refetch()}
                className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-50"
              >
                <RefreshCw aria-hidden="true" className="h-3.5 w-3.5" />
                Thử lại
              </button>
            </div>
          </Alert>
        )}

        {data && !isPending && !isError && (
          <>
            <SystemAdminStatCards metrics={data.metrics} />

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
              {/* Left column (2/3): Allowed Modules + Recent Audit Logs */}
              <div className="space-y-6 xl:col-span-2">
                <Card>
                  <CardHeader className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <h2 className="text-sm font-bold text-ink">
                        Lối vào chức năng Quản trị Hệ thống
                      </h2>
                      <p className="text-[11px] text-sub">
                        Các phân hệ được hiển thị tự động theo danh mục quyền RBAC của tài khoản
                      </p>
                    </div>
                    <Badge tone="sky">{allowedModules.length} phân hệ được phép</Badge>
                  </CardHeader>

                  <CardContent className="space-y-4 pt-4">
                    <SystemAdminAllowedModules
                      modules={allowedModules}
                      permissions={activePermissions}
                      selectedModuleCode={selectedModule?.moduleCode}
                      onSelectModule={setSelectedModuleOverride}
                    />

                    {selectedModule && (
                      <div className="flex flex-col justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50/60 p-3.5 sm:flex-row sm:items-center">
                        <div className="min-w-0 space-y-0.5">
                          <div className="font-mono text-[11px] font-bold uppercase text-primary">
                            Đang chọn phân hệ: {selectedModule.moduleCode} • {selectedModule.title}
                          </div>
                          <div className="text-xs text-sub">
                            Quyền yêu cầu:{' '}
                            <strong className="font-mono text-ink">
                              {selectedModule.requiredPermission}
                            </strong>{' '}
                            — Điều hướng: <span className="font-mono">{selectedModule.route}</span>
                          </div>
                        </div>

                        <Link
                          to={selectedModule.route}
                          className="inline-flex shrink-0 items-center gap-1 rounded-lg bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark"
                        >
                          <span>Đi tới {selectedModule.title}</span>
                          <ArrowUpRight aria-hidden="true" className="h-3.5 w-3.5" />
                        </Link>
                      </div>
                    )}
                  </CardContent>
                </Card>

                <Card>
                  <CardHeader className="space-y-3">
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                      <div>
                        <h2 className="text-sm font-bold text-ink">
                          Nhật ký kiểm toán &amp; Bảo mật gần đây
                        </h2>
                        <p className="text-[11px] text-sub">
                          {filteredAuditLogs.length} sự kiện hiển thị • giám sát xác thực, phân
                          quyền và cấu hình hệ thống
                        </p>
                      </div>
                      <Badge tone="slate">Tổng {data.metrics.totalAuditLogs} bản ghi</Badge>
                    </div>

                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="relative w-full sm:w-72">
                        <Search
                          aria-hidden="true"
                          className="pointer-events-none absolute top-1/2 left-2.5 h-3.5 w-3.5 -translate-y-1/2 text-sub"
                        />
                        <input
                          type="search"
                          value={searchQuery}
                          onChange={(e) => setSearchQuery(e.target.value)}
                          placeholder="Tìm mã sự kiện, hành động, email, IP..."
                          aria-label="Tìm kiếm nhật ký kiểm toán"
                          className="h-8 w-full rounded-lg border border-border bg-canvas pr-2.5 pl-8 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none"
                        />
                      </div>

                      <div
                        role="group"
                        aria-label="Lọc nhóm nhật ký kiểm toán"
                        className="flex flex-wrap items-center gap-1"
                      >
                        {AUDIT_CATEGORY_FILTER_OPTIONS.map((opt) => {
                          const active = categoryFilter === opt.id;
                          return (
                            <button
                              key={opt.id}
                              type="button"
                              aria-pressed={active}
                              onClick={() => setCategoryFilter(opt.id)}
                              className={`h-8 rounded-lg border px-2.5 text-xs font-semibold whitespace-nowrap transition-colors ${
                                active
                                  ? 'border-primary bg-primary text-white'
                                  : 'border-border bg-white text-sub hover:bg-slate-50 hover:text-ink'
                              }`}
                            >
                              {opt.label}
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  </CardHeader>

                  <CardContent className="space-y-4 px-6 pt-2 pb-5">
                    <SystemAdminAuditLogTable
                      logs={filteredAuditLogs}
                      selectedLogId={selectedLog?.id}
                      onSelectLog={setSelectedLogOverride}
                      onResetFilters={handleResetAuditTableFilters}
                    />

                    {selectedLog && (
                      <div className="flex flex-col justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50/60 p-3.5 sm:flex-row sm:items-center">
                        <div className="min-w-0 space-y-1">
                          <div className="flex items-center gap-1.5 font-mono text-[11px] font-bold uppercase text-primary">
                            <FileSearch aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
                            <span>
                              Chi tiết sự kiện: AUD-{String(selectedLog.id).padStart(4, '0')} •{' '}
                              {selectedLog.action}
                            </span>
                          </div>
                          <div className="text-xs font-medium text-ink">
                            Thực thể: <span className="font-mono">{selectedLog.entityType}</span>
                            {selectedLog.entityId ? ` (${selectedLog.entityId})` : ''} —{' '}
                            {selectedLog.reason ?? 'Không có ghi chú bổ sung'}
                          </div>
                          <div className="text-[11px] text-sub">
                            Tác nhân: {selectedLog.actorEmail ?? 'Hệ thống'} • IP:{' '}
                            <span className="font-mono">{selectedLog.ipAddress ?? 'N/A'}</span>
                          </div>
                        </div>
                      </div>
                    )}
                  </CardContent>
                </Card>
              </div>

              {/* Right column (1/3): Role Summary Card */}
              <div className="space-y-6">
                <SystemAdminRoleSummaryCard roles={data.roleSummaries} />
              </div>
            </div>
          </>
        )}
      </main>
    </AdminPortalShell>
  );
}
