import {
  AlertTriangle,
  ArrowUpRight,
  BarChart3,
  CalendarClock,
  FileCheck2,
  LogOut,
  RefreshCw,
  Search,
  ShieldCheck,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { HospitalAdminQuickActions } from '../components/hospital-admin-quick-actions';
import { HospitalAdminStatCards } from '../components/hospital-admin-stat-cards';
import { HospitalConfigWorklistTable } from '../components/hospital-config-worklist-table';
import { HOSPITAL_ADMIN_META } from '../data/hospital-admin-mock-data';
import { useHospitalAdminDashboard } from '../hooks/use-hospital-admin-dashboard';
import type {
  HospitalAdminQuickAction,
  HospitalConfigCategory,
  HospitalConfigStatus,
  HospitalConfigWorklistItem,
  HospitalPolicyAlertSeverity,
} from '../types/hospital-admin.types';

type CategoryFilter = 'ALL' | HospitalConfigCategory;
type StatusFilter = 'ALL' | HospitalConfigStatus;

const EMPTY_WORKLIST: HospitalConfigWorklistItem[] = [];

const CATEGORY_FILTER_OPTIONS: { id: CategoryFilter; label: string }[] = [
  { id: 'ALL', label: 'Tất cả' },
  { id: 'DEPARTMENT_ROOM', label: 'Khoa & Phòng' },
  { id: 'DOCTOR_SERVICE', label: 'Bác sĩ & Dịch vụ' },
  { id: 'SLOT_SCHEDULE', label: 'Lịch & Slot khám' },
  { id: 'POLICY_REPORT', label: 'Chính sách & Báo cáo' },
];

const POLICY_ALERT_META: Record<
  HospitalPolicyAlertSeverity,
  { label: string; tone: 'amber' | 'sky' | 'emerald' }
> = {
  WARNING: { label: 'Cần xử lý', tone: 'amber' },
  INFO: { label: 'Thông tin', tone: 'sky' },
  ACTIVE: { label: 'Đang hiệu lực', tone: 'emerald' },
};

export function HospitalAdminDashboardPage() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { data, isPending, isError, error, refetch } = useHospitalAdminDashboard(true);

  const [searchQuery, setSearchQuery] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('ALL');
  const [selectedItemOverride, setSelectedItemOverride] =
    useState<HospitalConfigWorklistItem | null>(null);
  const [selectedAction, setSelectedAction] = useState<HospitalAdminQuickAction | null>(null);

  const adminDisplayName = user?.fullName?.trim() || HOSPITAL_ADMIN_META.defaultAdminName;
  const staffCode = user?.staffCode?.trim() || HOSPITAL_ADMIN_META.defaultStaffCode;
  const facilityName = data?.facilityName || HOSPITAL_ADMIN_META.facilityName;

  const worklist = data?.worklist ?? EMPTY_WORKLIST;

  const filteredWorklist = useMemo(() => {
    const keyword = searchQuery.trim().toLowerCase();
    return worklist.filter((item) => {
      const matchesCategory = categoryFilter === 'ALL' || item.category === categoryFilter;
      const matchesStatus = statusFilter === 'ALL' || item.status === statusFilter;
      const haystack =
        `${item.code} ${item.name} ${item.departmentOrScope} ${item.metricLabel}`.toLowerCase();
      return matchesCategory && matchesStatus && (!keyword || haystack.includes(keyword));
    });
  }, [worklist, categoryFilter, statusFilter, searchQuery]);

  const selectedItem = selectedItemOverride ?? worklist[0] ?? null;

  const handleResetFilters = () => {
    setSearchQuery('');
    setCategoryFilter('ALL');
    setStatusFilter('ALL');
  };

  return (
    <AdminPortalShell
      activeTab="Tổng quan cơ sở"
      breadcrumbText="Tổng quan quản trị cơ sở"
      breadcrumbLink="/hospital-admin"
    >
      <main className="mx-auto max-w-[1680px] space-y-6 p-6">
        {/* Role Landing Header Card */}
        <section
          aria-label="Thông tin phiên làm việc Quản trị Bệnh viện"
          className="flex flex-col justify-between gap-4 rounded-2xl border border-border bg-white p-5 shadow-2xs lg:flex-row lg:items-center"
        >
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="sky">
                <ShieldCheck aria-hidden="true" className="mr-1 h-3.5 w-3.5" />
                Phân quyền: Quản trị bệnh viện (UC47–UC56)
              </Badge>
              <Badge tone="emerald">{facilityName}</Badge>
              <span className="rounded-md border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-ink">
                Mã NV: {staffCode}
              </span>
            </div>
            <h1 className="text-xl font-bold tracking-tight text-ink sm:text-2xl">
              Tổng quan quản trị bệnh viện
            </h1>
            <p className="text-xs text-sub">
              Xin chào, <strong className="font-semibold text-ink">{adminDisplayName}</strong> •{' '}
              {data?.shiftLabel ?? 'Điều phối danh mục khoa phòng, bác sĩ, dịch vụ và slot khám'}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <Link
              to="/admin/slots"
              className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark"
            >
              <CalendarClock aria-hidden="true" className="h-4 w-4" />
              Quản lý Slot khám
            </Link>
            <Link
              to="/admin/reports"
              className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-ink transition-colors hover:bg-slate-50"
            >
              <BarChart3 aria-hidden="true" className="h-4 w-4 text-primary" />
              Báo cáo vận hành
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

        {isPending && (
          <div
            role="status"
            className="rounded-2xl border border-border bg-white p-6 text-center text-xs font-medium text-sub"
          >
            Đang tải dữ liệu tổng quan quản trị bệnh viện...
          </div>
        )}

        {isError && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                {getErrorMessage(
                  error,
                  'Không thể tải dữ liệu tổng quan quản trị bệnh viện. Vui lòng thử lại.',
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
            <HospitalAdminStatCards stats={data.stats} />

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
              {/* Left column (2/3): Hospital Config Worklist + Selected Item Preview */}
              <Card className="xl:col-span-2">
                <CardHeader className="space-y-3">
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <h2 className="text-sm font-bold text-ink">
                        Danh mục cấu hình &amp; vận hành cơ sở
                      </h2>
                      <p className="text-[11px] text-sub">
                        {filteredWorklist.length} hạng mục hiển thị • phạm vi quản trị cấp cơ sở (
                        {data.facilityCode})
                      </p>
                    </div>

                    <div className="flex items-center gap-2">
                      <select
                        aria-label="Lọc trạng thái cấu hình"
                        value={statusFilter}
                        onChange={(e) => setStatusFilter(e.target.value as StatusFilter)}
                        className="h-8 rounded-lg border border-border bg-white px-2.5 text-xs font-medium text-ink focus:border-primary focus:outline-none"
                      >
                        <option value="ALL">Tất cả trạng thái</option>
                        <option value="ACTIVE">Đang hoạt động</option>
                        <option value="NEEDS_REVIEW">Cần rà soát</option>
                        <option value="MAINTENANCE">Tạm ngừng / Bảo trì</option>
                      </select>
                    </div>
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
                        placeholder="Tìm mã cấu hình, tên khoa, bác sĩ, dịch vụ..."
                        aria-label="Tìm kiếm hạng mục cấu hình cơ sở"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pr-2.5 pl-8 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <div
                      role="group"
                      aria-label="Lọc nhóm cấu hình bệnh viện"
                      className="flex flex-wrap items-center gap-1"
                    >
                      {CATEGORY_FILTER_OPTIONS.map((opt) => {
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
                  <HospitalConfigWorklistTable
                    entries={filteredWorklist}
                    selectedItemId={selectedItem?.id}
                    onSelectItem={setSelectedItemOverride}
                    onResetFilters={handleResetFilters}
                  />

                  {selectedItem && (
                    <div className="flex flex-col justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50/60 p-3.5 sm:flex-row sm:items-center">
                      <div className="min-w-0 space-y-1">
                        <div className="flex items-center gap-1.5 font-mono text-[11px] font-bold uppercase text-primary">
                          <FileCheck2 aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
                          <span>
                            Đang chọn: {selectedItem.code} • {selectedItem.name}
                          </span>
                        </div>
                        <div className="text-xs font-medium text-ink">
                          {selectedItem.departmentOrScope} — {selectedItem.metricLabel}
                        </div>
                        <div className="text-[11px] text-sub">
                          Cập nhật gần nhất: {selectedItem.updatedAt}
                        </div>
                      </div>

                      <Link
                        to={selectedItem.route}
                        className="inline-flex shrink-0 items-center gap-1 rounded-lg bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
                      >
                        <span>Đi tới màn hình cấu hình</span>
                        <ArrowUpRight aria-hidden="true" className="h-3.5 w-3.5" />
                      </Link>
                    </div>
                  )}
                </CardContent>
              </Card>

              {/* Right column (1/3): Quick Actions + Policy Alerts */}
              <div className="space-y-6">
                <Card>
                  <CardHeader>
                    <h2 className="text-sm font-bold text-ink">
                      Lối vào chức năng Quản trị Bệnh viện
                    </h2>
                    <p className="text-[11px] text-sub">
                      Truy cập nhanh các phân hệ cấu hình cơ sở được cấp quyền (UC47–UC56)
                    </p>
                  </CardHeader>
                  <CardContent className="space-y-3 pt-4">
                    <HospitalAdminQuickActions
                      actions={data.quickActions}
                      onAction={(action) => setSelectedAction(action)}
                    />
                    {selectedAction && (
                      <div className="flex items-center justify-between gap-2 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-[11px] text-sky-800">
                        <span>
                          Đang chọn lối vào:{' '}
                          <strong className="font-semibold">{selectedAction.label}</strong> (
                          {selectedAction.ucCode})
                        </span>
                        <Link
                          to={selectedAction.route}
                          className="shrink-0 font-bold text-primary underline hover:text-primary-dark"
                        >
                          Mở {selectedAction.label}
                        </Link>
                      </div>
                    )}
                  </CardContent>
                </Card>

                <Card>
                  <CardHeader className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <AlertTriangle aria-hidden="true" className="h-4 w-4 text-amber-600" />
                      <h2 className="text-sm font-bold text-ink">
                        Cảnh báo cấu hình &amp; Chính sách hẹn
                      </h2>
                    </div>
                    <Badge tone="amber">{data.policyAlerts.length} mục</Badge>
                  </CardHeader>
                  <CardContent className="p-0">
                    <ul className="divide-y divide-slate-100">
                      {data.policyAlerts.map((alert) => {
                        const alertMeta = POLICY_ALERT_META[alert.severity];
                        return (
                          <li
                            key={alert.id}
                            className="flex items-start justify-between gap-3 px-6 py-3"
                          >
                            <div className="min-w-0 space-y-0.5">
                              <div className="flex items-start gap-1.5">
                                <span className="mt-0.5 shrink-0 rounded border border-sky-200 bg-sky-50 px-1.5 py-0.5 font-mono text-[10px] font-bold whitespace-nowrap text-primary">
                                  {alert.code}
                                </span>
                                <span className="text-xs leading-snug font-bold text-ink">
                                  {alert.title}
                                </span>
                              </div>
                              <div className="text-[11px] text-sub">{alert.detail}</div>
                            </div>
                            <Badge tone={alertMeta.tone} className="shrink-0 whitespace-nowrap">
                              {alertMeta.label}
                            </Badge>
                          </li>
                        );
                      })}
                    </ul>
                    <div className="border-t border-border bg-canvas px-6 py-2.5 text-[11px] text-sub">
                      Giới hạn phạm vi RBAC: Chỉ hiển thị nghiệp vụ Quản trị Bệnh viện (UC47–UC56).
                      Các phân hệ Quản trị Hệ thống (UC57–UC60) và Bàn làm việc Lâm sàng được tách
                      biệt.
                    </div>
                  </CardContent>
                </Card>
              </div>
            </div>
          </>
        )}
      </main>
    </AdminPortalShell>
  );
}
