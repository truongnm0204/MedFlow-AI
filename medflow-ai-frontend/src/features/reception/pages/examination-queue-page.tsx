import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Building2, ListOrdered, RefreshCw, ShieldAlert } from 'lucide-react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasPermission } from '@/features/auth/utils/rbac';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { ExaminationQueueEmptyState } from '../components/examination-queue-empty-state';
import { ExaminationQueueFilterBar } from '../components/examination-queue-filter-bar';
import {
  EXAMINATION_QUEUE_STATUS_META,
  ExaminationQueueServicePointCard,
} from '../components/examination-queue-service-point-card';
import { ExaminationQueueStatCards } from '../components/examination-queue-stat-cards';
import { PatientDetailModal } from '../components/patient-detail-modal';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { UpdateQueueStatusModal } from '../components/update-queue-status-modal';
import { DEFAULT_QUEUE_DATE } from '../data/examination-queue-mock';
import { findPatientDetailByCode } from '../data/patient-detail-mock';
import { useExaminationQueue } from '../hooks/use-examination-queue';
import {
  examinationQueueFilterSchema,
  type ExaminationQueueFilterFormValues,
} from '../schemas/examination-queue-filter.schema';
import type {
  ExaminationQueueEntryDto,
  ExaminationQueueStatusFilter,
  ServicePointQueueDto,
} from '../types/examination-queue.types';

const INITIAL_FILTERS: ExaminationQueueFilterFormValues = {
  queueDate: DEFAULT_QUEUE_DATE,
  servicePointId: 'ALL',
  status: 'ALL',
  priority: 'ALL',
  visitType: 'ALL',
  keyword: '',
};

export function ExaminationQueuePage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const canManageQueue = hasPermission(user?.roles, 'QUEUE_MANAGE');

  const [filters, setFilters] = useState<ExaminationQueueFilterFormValues>(INITIAL_FILTERS);
  const [appliedFilters, setAppliedFilters] =
    useState<ExaminationQueueFilterFormValues>(INITIAL_FILTERS);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedPatientCode, setSelectedPatientCode] = useState<string | null>(null);
  const [editingEntry, setEditingEntry] = useState<ExaminationQueueEntryDto | null>(null);
  const [statusNotice, setStatusNotice] = useState<string | null>(null);
  const selectedPatient = selectedPatientCode ? findPatientDetailByCode(selectedPatientCode) : null;

  const { data, isPending, isError, error, refetch } = useExaminationQueue({
    queueDate: appliedFilters.queueDate,
  });

  const handleFilterChange = (patch: Partial<ExaminationQueueFilterFormValues>) => {
    const nextFilters = { ...filters, ...patch };
    setFilters(nextFilters);

    const parsed = examinationQueueFilterSchema.safeParse(nextFilters);
    if (!parsed.success) {
      setValidationError(parsed.error.issues[0]?.message ?? 'Bộ lọc không hợp lệ.');
      return;
    }

    setValidationError(null);
    setAppliedFilters(parsed.data);
  };

  const handleResetFilters = () => {
    setFilters(INITIAL_FILTERS);
    setAppliedFilters(INITIAL_FILTERS);
    setValidationError(null);
  };

  const servicePoints = useMemo(() => data?.servicePoints ?? [], [data]);

  const servicePointOptions = useMemo(() => {
    const totalCount = servicePoints.reduce((acc, sp) => acc + sp.entries.length, 0);
    return [
      {
        id: 'ALL',
        label: 'Tất cả phòng',
        roomCode: 'ALL',
        count: totalCount,
      },
      ...servicePoints.map((sp) => ({
        id: sp.servicePointId,
        label: `${sp.roomCode} • ${sp.roomName.replace('Phòng khám ', '')}`,
        roomCode: sp.roomCode,
        count: sp.entries.length,
      })),
    ];
  }, [servicePoints]);

  const statusCounts = useMemo<Record<ExaminationQueueStatusFilter, number>>(() => {
    const counts: Record<ExaminationQueueStatusFilter, number> = {
      ALL: 0,
      WAITING: 0,
      READY: 0,
      CALLED: 0,
      IN_PROGRESS: 0,
      COMPLETED: 0,
      SKIPPED: 0,
    };

    for (const sp of servicePoints) {
      if (
        appliedFilters.servicePointId !== 'ALL' &&
        sp.servicePointId !== appliedFilters.servicePointId
      ) {
        continue;
      }
      for (const entry of sp.entries) {
        counts.ALL += 1;
        counts[entry.status] += 1;
      }
    }

    return counts;
  }, [servicePoints, appliedFilters.servicePointId]);

  const visibleServicePoints = useMemo<ServicePointQueueDto[]>(() => {
    const kw = appliedFilters.keyword.trim().toLowerCase();
    const hasEntryFilter =
      appliedFilters.status !== 'ALL' ||
      appliedFilters.priority !== 'ALL' ||
      appliedFilters.visitType !== 'ALL' ||
      kw.length > 0;

    return servicePoints
      .filter(
        (sp) =>
          appliedFilters.servicePointId === 'ALL' ||
          sp.servicePointId === appliedFilters.servicePointId,
      )
      .map((sp) => {
        const filteredEntries = sp.entries
          .filter((entry) => {
            if (appliedFilters.status !== 'ALL' && entry.status !== appliedFilters.status) {
              return false;
            }
            if (appliedFilters.priority !== 'ALL' && entry.priority !== appliedFilters.priority) {
              return false;
            }
            if (
              appliedFilters.visitType !== 'ALL' &&
              entry.visitType !== appliedFilters.visitType
            ) {
              return false;
            }
            if (kw) {
              const matchesKeyword =
                entry.ticketNo.toLowerCase().includes(kw) ||
                entry.patientName.toLowerCase().includes(kw) ||
                entry.patientCode.toLowerCase().includes(kw) ||
                entry.visitCode.toLowerCase().includes(kw) ||
                entry.roomCode.toLowerCase().includes(kw);
              if (!matchesKeyword) return false;
            }
            return true;
          })
          .sort((a, b) => a.servingOrder - b.servingOrder);

        return {
          ...sp,
          entries: filteredEntries,
        };
      })
      .filter((sp) => (hasEntryFilter ? sp.entries.length > 0 : true));
  }, [servicePoints, appliedFilters]);

  const totalMatchingEntries = useMemo(
    () => visibleServicePoints.reduce((acc, sp) => acc + sp.entries.length, 0),
    [visibleServicePoints],
  );

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="queue"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'queue') navigate('/reception/queue');
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        {/* Top bar */}
        <header className="h-16 bg-white border-b border-border px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/reception')}
              aria-label="Quay lại tổng quan tiếp đón"
              className="p-2 rounded-xl text-sub hover:text-ink hover:bg-canvas transition-colors"
            >
              <ArrowLeft className="w-5 h-5" aria-hidden="true" />
            </button>
            <div>
              <div className="text-sm font-bold text-navy flex items-center gap-2">
                <span>Hàng đợi khám theo phòng & điểm dịch vụ (UC28)</span>
                {!canManageQueue && (
                  <Badge tone="amber">
                    <ShieldAlert className="w-3 h-3 mr-1" aria-hidden="true" />
                    Chế độ chỉ xem
                  </Badge>
                )}
              </div>
              <div className="text-[11px] text-sub">
                {data?.facilityName ?? 'Bệnh viện Đa khoa Trung ương MedFlow'} •{' '}
                {data?.shiftLabel ?? 'Ca sáng'} • Ngày {appliedFilters.queueDate}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Button variant="secondary" size="sm" onClick={() => void refetch()}>
              <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" />
              Làm mới
            </Button>

            <div className="flex items-center gap-2.5 pl-3 border-l border-border">
              <div className="w-9 h-9 rounded-full bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? 'LT').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-navy">
                  {user?.fullName ?? 'Nhân viên tiếp đón'}
                </div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-sub hover:text-rose-600 transition-colors"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        {/* Main Content */}
        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          {isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>{getErrorMessage(error, 'Không thể tải dữ liệu hàng đợi khám')}</span>
                  <Button variant="secondary" size="sm" onClick={() => void refetch()}>
                    Thử lại
                  </Button>
                </div>
              </Alert>
            </div>
          )}

          {statusNotice && (
            <div role="status">
              <Alert tone="success">{statusNotice}</Alert>
            </div>
          )}

          {isPending ? (
            <div
              role="status"
              className="bg-white rounded-2xl border border-border p-12 flex flex-col items-center justify-center gap-3 shadow-sm"
            >
              <Spinner className="h-6 w-6 text-primary" />
              <div className="text-xs font-medium text-sub">Đang tải dữ liệu hàng đợi khám...</div>
            </div>
          ) : (
            data && (
              <>
                <ExaminationQueueStatCards stats={data.stats} />

                <ExaminationQueueFilterBar
                  filters={filters}
                  servicePointOptions={servicePointOptions}
                  statusCounts={statusCounts}
                  validationError={validationError}
                  onFilterChange={handleFilterChange}
                  onReset={handleResetFilters}
                />

                <div className="grid grid-cols-1 xl:grid-cols-12 gap-6">
                  {/* Left: Service point queue tables */}
                  <div className="xl:col-span-8 space-y-6">
                    {totalMatchingEntries > 0 ? (
                      visibleServicePoints.map((servicePoint) => (
                        <ExaminationQueueServicePointCard
                          key={servicePoint.servicePointId}
                          servicePoint={servicePoint}
                          canManageQueue={canManageQueue}
                          onSelectPatient={setSelectedPatientCode}
                          onUpdateStatus={(entry) => {
                            setStatusNotice(null);
                            setEditingEntry(entry);
                          }}
                        />
                      ))
                    ) : (
                      <ExaminationQueueEmptyState
                        keyword={appliedFilters.keyword}
                        onReset={handleResetFilters}
                      />
                    )}
                  </div>

                  {/* Right: Room summary & Serving order legend */}
                  <div className="xl:col-span-4 space-y-6">
                    <Card>
                      <CardHeader className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Building2 className="w-4 h-4 text-primary" aria-hidden="true" />
                          <div className="text-sm font-bold text-navy">
                            Tổng hợp theo phòng khám
                          </div>
                        </div>
                        <Badge tone="sky">{servicePoints.length} phòng</Badge>
                      </CardHeader>
                      <CardContent className="p-0">
                        <ul className="divide-y divide-border">
                          {servicePoints.map((sp) => {
                            const isSelected = appliedFilters.servicePointId === sp.servicePointId;
                            return (
                              <li key={sp.servicePointId}>
                                <button
                                  type="button"
                                  onClick={() =>
                                    handleFilterChange({
                                      servicePointId: isSelected ? 'ALL' : sp.servicePointId,
                                    })
                                  }
                                  className={`w-full px-6 py-3.5 text-left flex items-center justify-between gap-3 transition-colors ${
                                    isSelected ? 'bg-primary/5' : 'hover:bg-canvas/60'
                                  }`}
                                >
                                  <div className="min-w-0">
                                    <div className="text-xs font-bold text-navy truncate">
                                      {sp.roomCode} — {sp.roomName}
                                    </div>
                                    <div className="text-[11px] text-sub truncate mt-0.5">
                                      {sp.doctorName} • {sp.departmentName}
                                    </div>
                                  </div>
                                  <div className="text-right shrink-0">
                                    <div className="text-[11px] font-mono font-bold text-emerald-700">
                                      Phục vụ: {sp.currentServingTicketNo ?? 'Chưa gọi'}
                                    </div>
                                    <div className="text-[10px] text-sub">
                                      {sp.waitingCount} đang chờ
                                    </div>
                                  </div>
                                </button>
                              </li>
                            );
                          })}
                        </ul>
                      </CardContent>
                    </Card>

                    <Card>
                      <CardHeader className="flex items-center gap-2">
                        <ListOrdered className="w-4 h-4 text-primary" aria-hidden="true" />
                        <div className="text-sm font-bold text-navy">
                          Quy tắc thứ tự phục vụ (UC28)
                        </div>
                      </CardHeader>
                      <CardContent className="space-y-2.5 text-xs text-sub leading-relaxed">
                        <p>
                          Thứ tự phục vụ (#1, #2, ...) tại mỗi điểm dịch vụ được sắp xếp tự động
                          theo nguyên tắc:
                        </p>
                        <ol className="list-decimal list-inside space-y-1.5 text-ink font-medium">
                          <li>Lượt đang khám (IN_PROGRESS) và đã gọi số (CALLED)</li>
                          <li>
                            Mức độ ưu tiên lâm sàng (Khẩn cấp, Cao tuổi, Thai phụ, Trả KQ CLS)
                          </li>
                          <li>Bệnh nhân đặt lịch hẹn đúng giờ (ON_TIME_OR_GRACE)</li>
                          <li>Thời điểm check-in thực tế tại quầy tiếp đón</li>
                        </ol>
                      </CardContent>
                    </Card>
                  </div>
                </div>
              </>
            )
          )}
        </main>

        <PatientDetailModal
          patient={selectedPatient}
          onClose={() => setSelectedPatientCode(null)}
        />

        <UpdateQueueStatusModal
          key={editingEntry?.id ?? 'closed'}
          open={Boolean(editingEntry)}
          entry={editingEntry}
          currentQueueData={data}
          onClose={() => setEditingEntry(null)}
          onUpdated={(result) => {
            setEditingEntry(null);
            const prevLabel = EXAMINATION_QUEUE_STATUS_META[result.previousStatus].label;
            const nextLabel = EXAMINATION_QUEUE_STATUS_META[result.entry.status].label;
            setStatusNotice(
              `Đã cập nhật trạng thái phiếu ${result.entry.ticketNo} (${result.entry.patientName}) từ ${prevLabel} sang ${nextLabel}.`,
            );
          }}
        />
      </div>
    </div>
  );
}
