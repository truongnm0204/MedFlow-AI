import { RefreshCw, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { DoctorDiagnosticTrackingStatCards } from '../components/doctor-diagnostic-tracking-stat-cards';
import { DoctorDiagnosticTrackingTable } from '../components/doctor-diagnostic-tracking-table';
import { DoctorDiagnosticVisitSummaryModal } from '../components/doctor-diagnostic-visit-summary-modal';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import { useDoctorDiagnosticTracking } from '../hooks/use-doctor-diagnostic-tracking';
import {
  DEFAULT_DOCTOR_DIAGNOSTIC_TRACKING_FILTERS,
  doctorDiagnosticTrackingFilterSchema,
  type DoctorDiagnosticTrackingFilterValues,
  toDoctorDiagnosticTrackingQueryParams,
} from '../schemas/doctor-diagnostic-tracking-filter.schema';
import type {
  DiagnosticTrackingStatusFilter,
  DiagnosticTrackingVisitDto,
} from '../types/doctor-diagnostic-tracking.types';

const FILTERS: { value: DiagnosticTrackingStatusFilter; label: string }[] = [
  { value: 'ALL', label: 'Tất cả' },
  { value: 'COMPLETED', label: 'Hoàn tất 100%' },
  { value: 'IN_PROGRESS', label: 'Đang thực hiện' },
  { value: 'PENDING_SIGN', label: 'Chờ ký duyệt' },
];

export function DoctorDiagnosticResultsTrackingPage() {
  const user = useAuthStore((s) => s.user);
  const navigate = useNavigate();
  const logout = useLogout();

  const [formFilters, setFormFilters] = useState<DoctorDiagnosticTrackingFilterValues>(
    DEFAULT_DOCTOR_DIAGNOSTIC_TRACKING_FILTERS,
  );
  const [validatedFilters, setValidatedFilters] = useState<DoctorDiagnosticTrackingFilterValues>(
    DEFAULT_DOCTOR_DIAGNOSTIC_TRACKING_FILTERS,
  );
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedVisit, setSelectedVisit] = useState<DiagnosticTrackingVisitDto | null>(null);

  const queryParams = useMemo(
    () => toDoctorDiagnosticTrackingQueryParams(validatedFilters),
    [validatedFilters],
  );
  const { data, isPending, isError, error, refetch } = useDoctorDiagnosticTracking(queryParams);

  const doctorName =
    data?.doctorName?.trim() || user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const handleFilterChange = (patch: Partial<DoctorDiagnosticTrackingFilterValues>) => {
    const resetsPage =
      patch.date !== undefined || patch.status !== undefined || patch.keyword !== undefined;
    const nextFilters: DoctorDiagnosticTrackingFilterValues = {
      ...formFilters,
      ...patch,
      ...(resetsPage && patch.page === undefined ? { page: 1 } : {}),
    };
    setFormFilters(nextFilters);

    const parsed = doctorDiagnosticTrackingFilterSchema.safeParse(nextFilters);
    if (!parsed.success) {
      setValidationError(
        parsed.error.issues[0]?.message ??
          'Tham số lọc theo dõi kết quả cận lâm sàng không hợp lệ.',
      );
      return;
    }

    setValidationError(null);
    setValidatedFilters(parsed.data);
  };

  const hasActiveFilter =
    validatedFilters.status !== 'ALL' || validatedFilters.keyword.trim() !== '';

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="diagnostics"
        doctorName={doctorName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') {
            navigate('/doctor');
            return;
          }
          if (item === 'assigned-queue') {
            navigate('/doctor/assigned-queue');
            return;
          }
          if (item === 'consultation') {
            navigate('/doctor/consultation');
          }
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div>
            <div className="text-sm font-bold text-navy">Theo dõi kết quả cận lâm sàng</div>
            <div className="text-[11px] text-sub">
              Tổng hợp chỉ định & kết quả CLS theo lượt khám • {data?.date ?? 'Hôm nay'}
            </div>
          </div>
          <div className="flex items-center gap-3 text-right">
            <button
              type="button"
              aria-label="Làm mới dữ liệu"
              onClick={() => void refetch()}
              disabled={isPending}
              className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas disabled:opacity-60"
            >
              <RefreshCw
                aria-hidden="true"
                className={`h-3.5 w-3.5 ${isPending ? 'animate-spin' : ''}`}
              />
            </button>
            <div className="text-xs font-bold text-navy">{doctorName}</div>
            <button
              type="button"
              onClick={() => logout.mutate()}
              disabled={logout.isPending}
              className="text-[10px] text-sub transition-colors hover:text-rose-600 disabled:opacity-60"
            >
              Đăng xuất
            </button>
          </div>
        </header>

        <main className="flex-1 space-y-6 overflow-y-auto p-6">
          {isPending && !data ? (
            <div className="flex flex-col items-center justify-center gap-3 py-16">
              <Spinner />
              <p className="text-xs text-sub">Đang tải dữ liệu theo dõi CLS...</p>
            </div>
          ) : isError ? (
            <div className="space-y-3">
              <div data-testid="tracking-error-banner">
                <Alert tone="error">{getErrorMessage(error)}</Alert>
              </div>
              <button
                type="button"
                onClick={() => void refetch()}
                className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
              >
                Thử lại
              </button>
            </div>
          ) : data ? (
            <>
              <DoctorDiagnosticTrackingStatCards stats={data.stats} />

              <Card>
                <CardHeader className="flex flex-col gap-3">
                  <div className="flex flex-wrap items-center gap-3">
                    <input
                      type="date"
                      aria-label="Ngày theo dõi"
                      value={formFilters.date || data.date || ''}
                      onChange={(e) => handleFilterChange({ date: e.target.value })}
                      className="h-9 rounded-lg border border-border bg-canvas px-2.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
                    />
                    <div className="relative min-w-[240px] flex-1 max-w-md">
                      <Search
                        className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
                        aria-hidden="true"
                      />
                      <input
                        type="search"
                        value={formFilters.keyword}
                        onChange={(e) => handleFilterChange({ keyword: e.target.value })}
                        placeholder="Tìm mã lượt khám, mã BN, tên BN, mã phiếu, dịch vụ..."
                        aria-label="Tìm kiếm lượt khám CLS"
                        className="h-9 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2" role="group" aria-label="Lọc trạng thái">
                    {FILTERS.map((f) => (
                      <button
                        key={f.value}
                        type="button"
                        aria-label={`Lọc ${f.label}`}
                        aria-pressed={formFilters.status === f.value}
                        onClick={() => handleFilterChange({ status: f.value })}
                        className={`rounded-full border px-3 py-1 text-[11px] font-semibold transition-colors ${
                          formFilters.status === f.value
                            ? 'border-primary bg-primary text-white'
                            : 'border-border bg-white text-navy hover:bg-canvas'
                        }`}
                      >
                        {f.label}
                      </button>
                    ))}
                  </div>
                  {validationError && (
                    <p
                      role="alert"
                      data-testid="tracking-filter-error"
                      className="text-xs text-rose-600"
                    >
                      {validationError}
                    </p>
                  )}
                </CardHeader>
                <CardContent className="pt-0">
                  {data.stats.totalVisits === 0 && !hasActiveFilter ? (
                    <div className="py-10 text-center text-sm text-sub">
                      Chưa có lượt khám nào có chỉ định CLS trong ngày này.
                    </div>
                  ) : (
                    <DoctorDiagnosticTrackingTable
                      visits={data.items}
                      page={data.page}
                      totalPages={data.totalPages}
                      totalCount={data.totalCount}
                      onPageChange={(page) => handleFilterChange({ page })}
                      onViewSummary={setSelectedVisit}
                    />
                  )}
                </CardContent>
              </Card>
            </>
          ) : null}
        </main>
      </div>

      <DoctorDiagnosticVisitSummaryModal
        visit={selectedVisit}
        onClose={() => setSelectedVisit(null)}
      />
    </div>
  );
}
