import { ArrowLeft, ListOrdered, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { DoctorAssignedQueueStatCards } from '../components/doctor-assigned-queue-stat-cards';
import { DoctorAssignedServicePointCard } from '../components/doctor-assigned-service-point-card';
import { DoctorCallNextBanner } from '../components/doctor-call-next-banner';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  useCallNextDoctorPatient,
  useDoctorAssignedQueue,
} from '../hooks/use-doctor-assigned-queue';
import type {
  DoctorAssignedQueueEntryDto,
  DoctorAssignedQueueStatsDto,
  DoctorAssignedServicePointDto,
  DoctorCallNextResponseDto,
  DoctorQueueGroupFilter,
} from '../types/doctor-assigned-queue.types';
import { QUEUE_GROUP_FILTER_OPTIONS } from '../utils/doctor-assigned-queue-meta';

const SERVING_ORDER_RULES = [
  'Đang khám / đã gọi số.',
  'Lịch hẹn được bảo vệ đã đến giờ.',
  'Người cao tuổi, thai phụ, tái khám đọc KQ CLS (chỉ khi còn khoảng trống an toàn trước lịch hẹn kế tiếp).',
  'Lịch hẹn đúng giờ.',
  'Safe Backfill.',
  'Thứ tự đến.',
] as const;

export function DoctorAssignedQueuePage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [queueDate, setQueueDate] = useState('');
  const [groupFilter, setGroupFilter] = useState<DoctorQueueGroupFilter>('ALL');
  const [searchTerm, setSearchTerm] = useState('');
  const [callingServicePointId, setCallingServicePointId] = useState<string | null>(null);
  const [callNextResult, setCallNextResult] = useState<DoctorCallNextResponseDto | null>(null);
  const [callNextError, setCallNextError] = useState<string | null>(null);
  const [entryOverridesByServicePoint, setEntryOverridesByServicePoint] = useState<
    Record<string, DoctorAssignedQueueEntryDto[]>
  >({});
  const callNextMutation = useCallNextDoctorPatient();

  const queryParams = useMemo(() => (queueDate ? { queueDate } : undefined), [queueDate]);
  const { data, isPending, isError, error, refetch } = useDoctorAssignedQueue(queryParams);

  const doctorDisplayName =
    data?.doctorName?.trim() || user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;

  const firstServicePoint = data?.servicePoints[0];
  const roomLabel = firstServicePoint
    ? `${firstServicePoint.roomCode} • ${firstServicePoint.departmentName}`
    : `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const facilityName = data?.facilityName || DOCTOR_WORKBENCH_META.facilityName;
  const shiftLabel = data?.shiftLabel || DOCTOR_WORKBENCH_META.shiftLabel;
  const displayQueueDate = data?.queueDate || queueDate || 'Hôm nay';

  const effectiveServicePoints = useMemo<DoctorAssignedServicePointDto[]>(() => {
    if (!data) return [];
    const dateKey = queueDate || data.queueDate;
    return data.servicePoints.map((sp) => {
      const overrideKey = `${dateKey}:${sp.servicePointId}`;
      const entries = entryOverridesByServicePoint[overrideKey];
      if (!entries) {
        return sp;
      }

      const inProgressCount = entries.filter(
        (e) => e.queueGroup === 'IN_PROGRESS' || e.status === 'IN_PROGRESS',
      ).length;
      const calledCount = entries.filter(
        (e) =>
          e.queueGroup === 'CALLED' || (e.status === 'CALLED' && e.queueGroup !== 'IN_PROGRESS'),
      ).length;
      const eligibleCount = entries.filter((e) => e.queueGroup === 'ELIGIBLE').length;
      const notEligibleCount = entries.filter((e) => e.queueGroup === 'NOT_ELIGIBLE').length;
      const skippedCount = entries.filter(
        (e) => e.queueGroup === 'SKIPPED' || e.status === 'SKIPPED',
      ).length;
      const completedCount = entries.filter(
        (e) => e.queueGroup === 'COMPLETED' || e.status === 'COMPLETED',
      ).length;
      const currentServingTicketNo =
        (
          entries.find((e) => e.status === 'IN_PROGRESS') ??
          entries.find((e) => e.status === 'CALLED')
        )?.ticketNo ?? null;
      const nextEligibleTicketNo =
        entries.find(
          (e) =>
            e.queueGroup === 'ELIGIBLE' ||
            (e.isEligible && (e.status === 'WAITING' || e.status === 'READY')),
        )?.ticketNo ?? null;

      return {
        ...sp,
        currentServingTicketNo,
        nextEligibleTicketNo,
        stats: {
          totalEntries: entries.length,
          inProgressCount,
          calledCount,
          eligibleCount,
          notEligibleCount,
          skippedCount,
          completedCount,
        },
        entries,
      };
    });
  }, [data, queueDate, entryOverridesByServicePoint]);

  const effectiveStats = useMemo<DoctorAssignedQueueStatsDto | null>(() => {
    if (!data) return null;
    return effectiveServicePoints.reduce<DoctorAssignedQueueStatsDto>(
      (acc, sp) => ({
        totalEntries: acc.totalEntries + sp.stats.totalEntries,
        inProgressCount: acc.inProgressCount + sp.stats.inProgressCount,
        calledCount: acc.calledCount + sp.stats.calledCount,
        eligibleCount: acc.eligibleCount + sp.stats.eligibleCount,
        notEligibleCount: acc.notEligibleCount + sp.stats.notEligibleCount,
        skippedCount: acc.skippedCount + sp.stats.skippedCount,
        completedCount: acc.completedCount + sp.stats.completedCount,
      }),
      {
        totalEntries: 0,
        inProgressCount: 0,
        calledCount: 0,
        eligibleCount: 0,
        notEligibleCount: 0,
        skippedCount: 0,
        completedCount: 0,
      },
    );
  }, [data, effectiveServicePoints]);

  const handleCallNext = async (servicePoint: DoctorAssignedServicePointDto) => {
    setCallNextResult(null);
    setCallNextError(null);
    setCallingServicePointId(servicePoint.servicePointId);

    try {
      const result = await callNextMutation.mutateAsync({
        servicePointId: servicePoint.servicePointId,
      });
      const dateKey = queueDate || data?.queueDate || '';
      const requestedKey = `${dateKey}:${servicePoint.servicePointId}`;
      const responseKey = `${dateKey}:${result.servicePointId}`;
      setEntryOverridesByServicePoint((prev) => ({
        ...prev,
        [requestedKey]: result.entries,
        [responseKey]: result.entries,
      }));
      setCallNextResult(result);
    } catch (err) {
      setCallNextError(getErrorMessage(err, 'Không thể gọi bệnh nhân tiếp theo.'));
    } finally {
      setCallingServicePointId(null);
    }
  };

  const visibleServicePoints = useMemo(() => {
    if (!data) return [];
    const keyword = searchTerm.trim().toLowerCase();
    const hasActiveFilter = groupFilter !== 'ALL' || keyword.length > 0;

    return effectiveServicePoints
      .map((sp) => {
        const entries = sp.entries.filter((entry) => {
          const matchesGroup = groupFilter === 'ALL' || entry.queueGroup === groupFilter;
          const matchesKeyword =
            keyword.length === 0 ||
            entry.ticketNo.toLowerCase().includes(keyword) ||
            entry.patientName.toLowerCase().includes(keyword) ||
            entry.patientCode.toLowerCase().includes(keyword) ||
            entry.visitCode.toLowerCase().includes(keyword);
          return matchesGroup && matchesKeyword;
        });
        return { ...sp, entries };
      })
      .filter((sp) => !hasActiveFilter || sp.entries.length > 0);
  }, [data, effectiveServicePoints, groupFilter, searchTerm]);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="assigned-queue"
        doctorName={doctorDisplayName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') {
            navigate('/doctor');
            return;
          }
          if (item === 'consultation') {
            navigate('/doctor/consultation');
          }
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-border px-6 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3 min-w-0">
            <button
              type="button"
              onClick={() => navigate('/doctor')}
              aria-label="Quay lại tổng quan bàn khám"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="text-sm font-bold text-navy truncate">
                Hàng đợi được phân công (UC30)
              </h1>
              <div className="text-[11px] text-sub truncate">
                {facilityName} • {shiftLabel} • Ngày {displayQueueDate}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <input
              type="date"
              aria-label="Ngày hàng đợi"
              value={queueDate || data?.queueDate || ''}
              onChange={(e) => setQueueDate(e.target.value)}
              className="rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-medium text-ink focus:border-primary focus:outline-none"
            />
            <button
              type="button"
              onClick={() => void refetch()}
              disabled={isPending}
              aria-label="Làm mới hàng đợi"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas disabled:opacity-60"
            >
              <RefreshCw
                aria-hidden="true"
                className={`h-3.5 w-3.5 ${isPending ? 'animate-spin' : ''}`}
              />
              <span className="hidden sm:inline">Làm mới</span>
            </button>
            <div className="flex items-center gap-2.5 pl-3 border-l border-border">
              <div className="w-9 h-9 rounded-full bg-primary/10 border border-primary/20 text-primary flex items-center justify-center font-bold text-xs">
                {doctorDisplayName.slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-navy">{doctorDisplayName}</div>
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

        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          {isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>{getErrorMessage(error, 'Không thể tải hàng đợi được phân công')}</span>
                  <button
                    type="button"
                    onClick={() => void refetch()}
                    className="rounded-lg bg-rose-600 px-3 py-1 text-xs font-semibold text-white transition-colors hover:bg-rose-700"
                  >
                    Thử lại
                  </button>
                </div>
              </Alert>
            </div>
          )}

          {isPending ? (
            <div
              role="status"
              className="bg-white rounded-2xl border border-border p-12 flex flex-col items-center justify-center gap-3 shadow-sm"
            >
              <Spinner className="h-6 w-6 text-primary" />
              <div className="text-xs font-medium text-sub">
                Đang tải hàng đợi được phân công...
              </div>
            </div>
          ) : (
            data && (
              <>
                <DoctorCallNextBanner
                  result={callNextResult}
                  error={callNextError}
                  onDismissResult={() => setCallNextResult(null)}
                  onDismissError={() => setCallNextError(null)}
                  onOpenConsultation={(visitCode) =>
                    navigate(`/doctor/consultation/${encodeURIComponent(visitCode)}`)
                  }
                  onViewMedicalHistory={(entry) =>
                    navigate(
                      `/doctor/visits/${encodeURIComponent(entry.visitCode)}/medical-history`,
                    )
                  }
                />

                {effectiveStats && <DoctorAssignedQueueStatCards stats={effectiveStats} />}
                <Card>
                  <CardContent className="py-4 flex flex-wrap items-center justify-between gap-3">
                    <div className="flex flex-wrap gap-1.5">
                      {QUEUE_GROUP_FILTER_OPTIONS.map((opt) => (
                        <button
                          key={opt.id}
                          type="button"
                          aria-pressed={groupFilter === opt.id}
                          onClick={() => setGroupFilter(opt.id)}
                          className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-colors ${
                            groupFilter === opt.id
                              ? 'bg-primary text-white'
                              : 'bg-canvas text-sub hover:bg-border/60'
                          }`}
                        >
                          {opt.label} ({(effectiveStats ?? data.stats)[opt.statKey]})
                        </button>
                      ))}
                    </div>
                    <input
                      type="search"
                      value={searchTerm}
                      onChange={(e) => setSearchTerm(e.target.value)}
                      placeholder="Tìm tên, mã BN, số phiếu..."
                      aria-label="Tìm bệnh nhân trong hàng đợi"
                      className="w-full sm:w-64 px-3 py-1.5 rounded-lg border border-border text-xs text-ink focus:outline-none focus:border-primary"
                    />
                  </CardContent>
                </Card>

                <div className="grid grid-cols-1 xl:grid-cols-12 gap-6">
                  <div className="xl:col-span-8 space-y-6">
                    {data.servicePoints.length === 0 ? (
                      <Card>
                        <CardContent className="py-12 text-center text-xs text-sub">
                          Chưa có điểm khám nào được phân công cho bác sĩ trong ngày này.
                        </CardContent>
                      </Card>
                    ) : visibleServicePoints.length === 0 ? (
                      <Card>
                        <CardContent className="py-12 text-center text-xs text-sub">
                          Không có bệnh nhân phù hợp bộ lọc hiện tại.
                        </CardContent>
                      </Card>
                    ) : (
                      visibleServicePoints.map((sp) => (
                        <DoctorAssignedServicePointCard
                          key={sp.servicePointId}
                          servicePoint={sp}
                          onCallNext={(servicePoint) => void handleCallNext(servicePoint)}
                          isCallingNext={callingServicePointId === sp.servicePointId}
                          onOpenConsultation={(entry) =>
                            navigate(`/doctor/consultation/${encodeURIComponent(entry.visitCode)}`)
                          }
                          onViewMedicalHistory={(entry) =>
                            navigate(
                              `/doctor/visits/${encodeURIComponent(entry.visitCode)}/medical-history`,
                            )
                          }
                        />
                      ))
                    )}
                  </div>

                  <div className="xl:col-span-4 space-y-6">
                    <Card>
                      <CardHeader className="flex items-center gap-2">
                        <ListOrdered className="h-4 w-4 text-primary" aria-hidden="true" />
                        <div className="text-sm font-bold text-navy">
                          Quy tắc thứ tự phục vụ (UC30)
                        </div>
                      </CardHeader>
                      <CardContent className="pt-0 space-y-3">
                        <ol className="list-decimal list-inside space-y-2 text-xs text-ink">
                          {SERVING_ORDER_RULES.map((rule) => (
                            <li key={rule} className="leading-relaxed">
                              {rule}
                            </li>
                          ))}
                        </ol>
                        <div className="rounded-xl bg-canvas border border-border px-3 py-2 text-[11px] font-medium text-sub">
                          Thứ tự do Backend tính; màn hình không tự sắp xếp lại.
                        </div>
                      </CardContent>
                    </Card>
                  </div>
                </div>
              </>
            )
          )}
        </main>
      </div>
    </div>
  );
}
