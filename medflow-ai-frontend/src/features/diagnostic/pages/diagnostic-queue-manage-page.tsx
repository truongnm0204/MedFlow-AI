import { ArrowLeft, CheckCircle2, Filter, RefreshCw, RotateCcw, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticQueueEntryDetailPanel } from '../components/diagnostic-queue-entry-detail-panel';
import {
  DiagnosticQueueManageTable,
  type QuickQueueRowAction,
} from '../components/diagnostic-queue-manage-table';
import { DiagnosticQueueServicePointCards } from '../components/diagnostic-queue-service-point-cards';
import { DiagnosticQueueStatCards } from '../components/diagnostic-queue-stat-cards';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useCallDiagnosticQueueEntry,
  useCallNextDiagnosticPatient,
  useCancelDiagnosticQueueEntry,
  useCompleteDiagnosticQueueExecution,
  useDiagnosticQueueBoard,
  useDiagnosticQueueEntryDetail,
  useMarkDiagnosticQueueMissedTurn,
  useMarkDiagnosticQueueReady,
  useRestoreDiagnosticQueueTurn,
  useStartDiagnosticQueueExecution,
  useUpdateDiagnosticQueueStatus,
} from '../hooks/use-diagnostic-queue';
import {
  DEFAULT_DIAGNOSTIC_QUEUE_FILTERS,
  diagnosticQueueFilterSchema,
  toDiagnosticQueueQueryParams,
  type DiagnosticQueueFilterFormValues,
} from '../schemas/diagnostic-queue.schema';
import type {
  CallNextDiagnosticPatientResponseDto,
  CancelDiagnosticQueueEntryRequest,
  DiagnosticQueueCategoryFilter,
  DiagnosticQueueEntryItemDto,
  DiagnosticQueuePresenceFilter,
  DiagnosticQueuePriorityFilter,
  DiagnosticQueueStatusFilter,
  DiagnosticQueueSummaryStatsDto,
  DiagnosticQueueTurnActionRequest,
  DiagnosticServicePointQueueBucketDto,
  ManageDiagnosticQueueActionResponseDto,
  UpdateDiagnosticQueueStatusRequest,
} from '../types/diagnostic.types';

const MANAGE_QUEUE_REQUIRED_PERMISSIONS = ['QUEUE_MANAGE', 'DIAGNOSTIC_RESULT_WRITE'];

const DEFAULT_STATS: DiagnosticQueueSummaryStatsDto = {
  totalEntries: 0,
  waitingCount: 0,
  readyCount: 0,
  calledCount: 0,
  inProgressCount: 0,
  completedCount: 0,
  skippedCount: 0,
  cancelledCount: 0,
  urgentOrStatCount: 0,
  mandatoryCount: 0,
  activeServicePointsCount: 0,
  busyServicePointsCount: 0,
  laboratoryCount: 0,
  imagingCount: 0,
  functionalCount: 0,
};

export function DiagnosticQueueManagePage() {
  const { entryIdOrCode } = useParams<{ entryIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [formFilters, setFormFilters] = useState<DiagnosticQueueFilterFormValues>(
    DEFAULT_DIAGNOSTIC_QUEUE_FILTERS,
  );
  const [validatedFilters, setValidatedFilters] = useState<DiagnosticQueueFilterFormValues>(
    DEFAULT_DIAGNOSTIC_QUEUE_FILTERS,
  );
  const [filterValidationError, setFilterValidationError] = useState<string | null>(null);

  const [selectedEntryKey, setSelectedEntryKey] = useState<string | null>(null);
  const [callingServicePointId, setCallingServicePointId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastCallNextResult, setLastCallNextResult] =
    useState<CallNextDiagnosticPatientResponseDto | null>(null);
  const [lastActionResult, setLastActionResult] =
    useState<ManageDiagnosticQueueActionResponseDto | null>(null);

  const queryParams = useMemo(
    () => toDiagnosticQueueQueryParams(validatedFilters),
    [validatedFilters],
  );

  const boardQuery = useDiagnosticQueueBoard(queryParams, true);

  const callNextMutation = useCallNextDiagnosticPatient();
  const markReadyMutation = useMarkDiagnosticQueueReady();
  const callEntryMutation = useCallDiagnosticQueueEntry();
  const startExecutionMutation = useStartDiagnosticQueueExecution();
  const completeExecutionMutation = useCompleteDiagnosticQueueExecution();
  const missedTurnMutation = useMarkDiagnosticQueueMissedTurn();
  const restoreTurnMutation = useRestoreDiagnosticQueueTurn();
  const cancelEntryMutation = useCancelDiagnosticQueueEntry();
  const updateStatusMutation = useUpdateDiagnosticQueueStatus();

  const isMutating =
    callNextMutation.isPending ||
    markReadyMutation.isPending ||
    callEntryMutation.isPending ||
    startExecutionMutation.isPending ||
    completeExecutionMutation.isPending ||
    missedTurnMutation.isPending ||
    restoreTurnMutation.isPending ||
    cancelEntryMutation.isPending ||
    updateStatusMutation.isPending;

  const entries = useMemo(
    () => boardQuery.data?.entries ?? boardQuery.data?.items ?? [],
    [boardQuery.data?.entries, boardQuery.data?.items],
  );
  const stats = boardQuery.data?.stats ?? DEFAULT_STATS;
  const servicePoints = boardQuery.data?.servicePoints ?? [];
  const availableServicePoints = boardQuery.data?.availableServicePoints ?? [];

  const resolvedEntryKey = useMemo(() => {
    const manualOrRoute = selectedEntryKey ?? entryIdOrCode?.trim() ?? null;
    if (entries.length === 0) {
      return entryIdOrCode?.trim() || null;
    }

    if (manualOrRoute) {
      const normalized = manualOrRoute.toUpperCase();
      const matched = entries.find(
        (item) =>
          item.formattedQueueEntryId.toUpperCase() === normalized ||
          item.id.toUpperCase() === normalized ||
          String(item.queueEntryId) === normalized ||
          item.ticketNo.toUpperCase() === normalized ||
          (item.orderCode && item.orderCode.toUpperCase() === normalized),
      );
      if (matched) {
        return matched.formattedQueueEntryId;
      }
      if (entryIdOrCode?.trim() && !selectedEntryKey) {
        return entryIdOrCode.trim();
      }
    }

    const firstActive =
      entries.find((item) => item.status === 'READY' || item.status === 'CALLED') ?? entries[0];
    return firstActive.formattedQueueEntryId;
  }, [entries, selectedEntryKey, entryIdOrCode]);

  const detailQuery = useDiagnosticQueueEntryDetail(resolvedEntryKey, Boolean(resolvedEntryKey));

  const activeEntry: DiagnosticQueueEntryItemDto | null = useMemo(() => {
    const normalized = resolvedEntryKey?.toUpperCase();
    const fromList = entries.find(
      (item) =>
        normalized &&
        (item.formattedQueueEntryId.toUpperCase() === normalized ||
          item.id.toUpperCase() === normalized ||
          String(item.queueEntryId) === normalized ||
          item.ticketNo.toUpperCase() === normalized ||
          (item.orderCode && item.orderCode.toUpperCase() === normalized)),
    );
    if (detailQuery.data?.entry) {
      if (
        !fromList ||
        detailQuery.data.entry.formattedQueueEntryId === fromList.formattedQueueEntryId
      ) {
        return detailQuery.data.entry;
      }
    }
    return fromList ?? null;
  }, [entries, resolvedEntryKey, detailQuery.data]);

  const staffDisplayName =
    user?.fullName?.trim() ||
    boardQuery.data?.staffProfile?.fullName?.trim() ||
    'CN. Lê Hoàng Minh';
  const staffCode =
    user?.staffCode?.trim() || boardQuery.data?.staffProfile?.staffCode?.trim() || 'DIAG-009';
  const facilityName =
    boardQuery.data?.facilityName ||
    boardQuery.data?.hospitalName ||
    boardQuery.data?.staffProfile?.hospitalName ||
    'Bệnh viện Đại học Y Dược MedFlow';
  const departmentName =
    boardQuery.data?.departmentName ||
    boardQuery.data?.staffProfile?.departmentName ||
    'Khoa Xét nghiệm & Chẩn đoán hình ảnh';
  const shiftLabel = boardQuery.data?.shiftLabel || 'Ca sáng (07:00 - 11:30)';

  const hasManagePermission = useMemo(() => {
    const userPerms = user?.permissions;
    const profilePerms = boardQuery.data?.staffProfile?.permissions;
    const effectivePerms =
      userPerms !== undefined ? userPerms : (profilePerms ?? MANAGE_QUEUE_REQUIRED_PERMISSIONS);

    return effectivePerms.some((perm: string) =>
      MANAGE_QUEUE_REQUIRED_PERMISSIONS.includes(perm.trim().toUpperCase()),
    );
  }, [user?.permissions, boardQuery.data?.staffProfile?.permissions]);

  const handleFilterChange = (patch: Partial<DiagnosticQueueFilterFormValues>) => {
    const nextFilters = { ...formFilters, ...patch };
    setFormFilters(nextFilters);

    const parsed = diagnosticQueueFilterSchema.safeParse(nextFilters);
    if (!parsed.success) {
      setFilterValidationError(
        parsed.error.issues[0]?.message ?? 'Bộ lọc hàng đợi cận lâm sàng không hợp lệ.',
      );
      return;
    }

    setFilterValidationError(null);
    setValidatedFilters(parsed.data);
  };

  const handleResetFilters = () => {
    setFormFilters(DEFAULT_DIAGNOSTIC_QUEUE_FILTERS);
    setValidatedFilters(DEFAULT_DIAGNOSTIC_QUEUE_FILTERS);
    setFilterValidationError(null);
  };

  const handleSelectEntry = (entry: DiagnosticQueueEntryItemDto) => {
    setSelectedEntryKey(entry.formattedQueueEntryId);
    setActionError(null);
  };

  const handleCallNext = async (bucket: DiagnosticServicePointQueueBucketDto) => {
    setActionError(null);
    setLastActionResult(null);
    setCallingServicePointId(bucket.formattedServicePointId);

    try {
      const response = await callNextMutation.mutateAsync({
        servicePointId: bucket.formattedServicePointId,
        payload: {
          servicePointId: bucket.formattedServicePointId,
          category: validatedFilters.category === 'ALL' ? undefined : validatedFilters.category,
        },
      });
      setLastCallNextResult(response);
      if (response.calledEntry) {
        setSelectedEntryKey(response.calledEntry.formattedQueueEntryId);
      }
    } catch (error) {
      setActionError(
        getErrorMessage(error, 'Không thể gọi bệnh nhân tiếp theo tại điểm phục vụ cận lâm sàng.'),
      );
    } finally {
      setCallingServicePointId(null);
    }
  };

  const runMutation = async (
    actionPromise: Promise<ManageDiagnosticQueueActionResponseDto>,
    fallbackMessage: string,
  ) => {
    setActionError(null);
    setLastCallNextResult(null);
    try {
      const result = await actionPromise;
      setLastActionResult(result);
      setSelectedEntryKey(result.entry.formattedQueueEntryId);
    } catch (error) {
      setActionError(getErrorMessage(error, fallbackMessage));
    }
  };

  const handleQuickRowAction = (
    entry: DiagnosticQueueEntryItemDto,
    action: QuickQueueRowAction,
  ) => {
    setSelectedEntryKey(entry.formattedQueueEntryId);

    if (action === 'ready') {
      void runMutation(
        markReadyMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: { presenceStatus: 'PRESENT', expectedStateVersion: entry.stateVersion },
        }),
        'Không thể đánh dấu bệnh nhân sẵn sàng trong hàng đợi.',
      );
      return;
    }

    if (action === 'call') {
      void runMutation(
        callEntryMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: { presenceStatus: 'PRESENT', expectedStateVersion: entry.stateVersion },
        }),
        'Không thể gọi bệnh nhân vào phòng kỹ thuật.',
      );
      return;
    }

    if (action === 'start') {
      void runMutation(
        startExecutionMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: { presenceStatus: 'PRESENT', expectedStateVersion: entry.stateVersion },
        }),
        'Không thể bắt đầu thực hiện kỹ thuật cận lâm sàng.',
      );
      return;
    }

    if (action === 'complete') {
      void runMutation(
        completeExecutionMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: { presenceStatus: 'PRESENT', expectedStateVersion: entry.stateVersion },
        }),
        'Không thể hoàn tất thực hiện lượt xếp hàng cận lâm sàng.',
      );
      return;
    }

    if (action === 'restore-turn') {
      void runMutation(
        restoreTurnMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: {
            presenceStatus: 'PRESENT',
            reason: 'Bệnh nhân đã quay lại phòng cận lâm sàng và được khôi phục lượt chờ.',
            expectedStateVersion: entry.stateVersion,
          },
        }),
        'Không thể khôi phục lượt chờ cho bệnh nhân.',
      );
      return;
    }

    if (action === 'missed-turn') {
      void runMutation(
        missedTurnMutation.mutateAsync({
          idOrCode: entry.formattedQueueEntryId,
          payload: {
            presenceStatus: 'LEFT',
            reason: 'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
            blockedReason: 'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
            expectedStateVersion: entry.stateVersion,
          },
        }),
        'Không thể đánh dấu bệnh nhân qua lượt.',
      );
    }
  };

  const handleSidebarNavigation = (navItem: string) => {
    if (navItem === 'dashboard') {
      navigate('/diagnostic');
    } else if (navItem === 'orders') {
      navigate('/diagnostic/orders');
    } else if (navItem === 'queue') {
      navigate('/diagnostic/queue');
    } else if (navItem === 'execution') {
      navigate('/diagnostic/execution');
    } else if (navItem === 'results') {
      navigate('/diagnostic/results');
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem="queue"
        onItemSelect={handleSidebarNavigation}
        staffName={staffDisplayName}
        departmentName={departmentName}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top bar */}
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/diagnostic')}
              aria-label="Quay lại tổng quan cận lâm sàng"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h1 className="truncate text-sm font-bold text-navy">
                  Quản lý & Điều phối Hàng đợi Cận lâm sàng
                </h1>
                <Badge tone="sky">UC41</Badge>
              </div>
              <div className="truncate text-[11px] text-sub">
                {facilityName} • {departmentName} • {shiftLabel}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => void boardQuery.refetch()}
              aria-label="Làm mới bảng hàng đợi cận lâm sàng"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
            >
              <RefreshCw
                className={`h-3.5 w-3.5 ${boardQuery.isFetching ? 'animate-spin' : ''}`}
                aria-hidden="true"
              />
              <span className="hidden sm:inline">Làm mới</span>
            </button>

            <div className="text-right">
              <div className="text-xs font-bold text-navy">{staffDisplayName}</div>
              <div className="font-mono text-[10px] text-sub">Mã NV: {staffCode}</div>
            </div>
            <button
              type="button"
              onClick={() => logout.mutate()}
              className="rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-sub transition-colors hover:border-rose-200 hover:bg-rose-50 hover:text-rose-600"
            >
              Đăng xuất
            </button>
          </div>
        </header>

        <main className="flex-1 space-y-5 overflow-y-auto p-6">
          {/* Board Load Error Banner */}
          {boardQuery.isError && (
            <Alert tone="error">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="queue-board-error-banner"
              >
                <span>
                  {getErrorMessage(
                    boardQuery.error,
                    'Không thể tải bảng hàng đợi cận lâm sàng. Vui lòng thử lại.',
                  )}
                </span>
                <button
                  type="button"
                  onClick={() => void boardQuery.refetch()}
                  className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-50"
                >
                  Thử lại
                </button>
              </div>
            </Alert>
          )}

          {/* Filter Validation Error Banner */}
          {filterValidationError && (
            <Alert tone="error">
              <div data-testid="queue-filter-validation-error">{filterValidationError}</div>
            </Alert>
          )}

          {/* Action / Mutation Error Banner */}
          {actionError && (
            <Alert tone="error">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="queue-action-error-banner"
              >
                <span>{actionError}</span>
                <button
                  type="button"
                  onClick={() => setActionError(null)}
                  className="rounded-lg border border-rose-300 bg-white px-2.5 py-1 text-xs font-semibold text-rose-700 hover:bg-rose-50"
                >
                  Đóng
                </button>
              </div>
            </Alert>
          )}

          {/* Call Next Result Banner */}
          {lastCallNextResult && (
            <div role="status" aria-live="polite">
              <Alert tone={lastCallNextResult.outcome === 'CALLED' ? 'success' : 'info'}>
                <div
                  className="flex flex-wrap items-center justify-between gap-3"
                  data-testid="call-next-result-banner"
                >
                  <div className="flex items-center gap-2 text-xs">
                    <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>
                      <strong>{lastCallNextResult.message}</strong>
                      {lastCallNextResult.calledEntry && (
                        <>
                          {' '}
                          • Số TT:{' '}
                          <span className="font-mono font-bold">
                            {lastCallNextResult.calledEntry.ticketNo}
                          </span>{' '}
                          • Mã lượt:{' '}
                          <span className="font-mono">
                            {lastCallNextResult.calledEntry.formattedQueueEntryId}
                          </span>{' '}
                          • Phiên bản:{' '}
                          <span className="font-mono">v{lastCallNextResult.stateVersion}</span>
                        </>
                      )}
                    </span>
                  </div>
                  <button
                    type="button"
                    onClick={() => setLastCallNextResult(null)}
                    className="text-xs font-semibold underline"
                  >
                    Ẩn thông báo
                  </button>
                </div>
              </Alert>
            </div>
          )}

          {/* Mutation Action Success Banner */}
          {lastActionResult && (
            <div role="status" aria-live="polite">
              <Alert tone="success">
                <div
                  className="flex flex-wrap items-center justify-between gap-3"
                  data-testid="queue-action-success-banner"
                >
                  <div className="flex items-center gap-2 text-xs">
                    <CheckCircle2
                      className="h-4 w-4 shrink-0 text-emerald-600"
                      aria-hidden="true"
                    />
                    <span>
                      {lastActionResult.message} • Chuyển trạng thái:{' '}
                      <span className="font-mono font-bold">
                        {lastActionResult.previousStatus} → {lastActionResult.currentStatus}
                      </span>{' '}
                      • Phiên bản:{' '}
                      <span className="font-mono">v{lastActionResult.stateVersion}</span>
                    </span>
                  </div>
                  <button
                    type="button"
                    onClick={() => setLastActionResult(null)}
                    className="text-xs font-semibold text-emerald-800 underline hover:text-emerald-950"
                  >
                    Ẩn thông báo
                  </button>
                </div>
              </Alert>
            </div>
          )}

          {/* 4 KPI Summary Stat Cards */}
          <DiagnosticQueueStatCards stats={stats} />

          {/* Service Point Queue Buckets Strip */}
          <DiagnosticQueueServicePointCards
            buckets={servicePoints}
            selectedServicePointId={formFilters.servicePointId}
            hasManagePermission={hasManagePermission}
            callingServicePointId={callingServicePointId}
            onSelectServicePoint={(servicePointId) => handleFilterChange({ servicePointId })}
            onCallNext={(bucket) => void handleCallNext(bucket)}
          />

          {/* Main 12-column Clinical Workbench Grid */}
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
            {/* Left 7 columns: Filter Bar + Queue Entries Table */}
            <div className="xl:col-span-7">
              <Card>
                <CardHeader className="space-y-3 border-b border-border pb-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-bold text-navy">
                        Danh sách lượt xếp hàng thực hiện cận lâm sàng
                      </div>
                      <div className="text-[11px] text-sub">
                        Hiển thị {entries.length} lượt xếp hàng • Sắp xếp theo thứ tự ưu tiên phục
                        vụ (STAT → URGENT → NORMAL)
                      </div>
                    </div>

                    {boardQuery.data?.queueDate && (
                      <Badge tone="slate" className="font-mono">
                        Ngày hàng đợi: {boardQuery.data.queueDate}
                      </Badge>
                    )}
                  </div>

                  {/* Filter Bar */}
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="relative min-w-[200px] flex-1">
                      <Search
                        className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-sub"
                        aria-hidden="true"
                      />
                      <input
                        type="search"
                        value={formFilters.keyword}
                        onChange={(e) => handleFilterChange({ keyword: e.target.value })}
                        placeholder="Tìm số TT (C018), mã QE/CĐ, tên/mã BN, dịch vụ..."
                        aria-label="Tìm kiếm lượt xếp hàng cận lâm sàng"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <select
                      value={formFilters.category}
                      onChange={(e) =>
                        handleFilterChange({
                          category: e.target.value as DiagnosticQueueCategoryFilter,
                        })
                      }
                      aria-label="Lọc nhóm cận lâm sàng"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả nhóm CLS</option>
                      <option value="LABORATORY">Xét nghiệm</option>
                      <option value="IMAGING">Chẩn đoán hình ảnh</option>
                      <option value="FUNCTIONAL">Thăm dò chức năng</option>
                    </select>

                    <select
                      value={formFilters.queueStatus}
                      onChange={(e) =>
                        handleFilterChange({
                          queueStatus: e.target.value as DiagnosticQueueStatusFilter,
                        })
                      }
                      aria-label="Lọc trạng thái hàng đợi"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi trạng thái hàng đợi</option>
                      <option value="READY">Sẵn sàng gọi (READY)</option>
                      <option value="WAITING">Chờ tiếp nhận (WAITING)</option>
                      <option value="CALLED">Đã gọi vào phòng (CALLED)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="SKIPPED">Qua lượt / Vắng mặt (SKIPPED)</option>
                      <option value="COMPLETED">Đã hoàn tất (COMPLETED)</option>
                      <option value="CANCELLED">Đã hủy lượt (CANCELLED)</option>
                    </select>

                    <select
                      value={formFilters.priority}
                      onChange={(e) =>
                        handleFilterChange({
                          priority: e.target.value as DiagnosticQueuePriorityFilter,
                        })
                      }
                      aria-label="Lọc mức độ ưu tiên"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi mức ưu tiên</option>
                      <option value="STAT">Cấp cứu (STAT)</option>
                      <option value="URGENT">Khẩn (URGENT)</option>
                      <option value="ELDERLY">Người cao tuổi (ELDERLY)</option>
                      <option value="PREGNANT">Thai phụ (PREGNANT)</option>
                      <option value="NORMAL">Bình thường (NORMAL)</option>
                    </select>

                    <select
                      value={formFilters.presenceStatus}
                      onChange={(e) =>
                        handleFilterChange({
                          presenceStatus: e.target.value as DiagnosticQueuePresenceFilter,
                        })
                      }
                      aria-label="Lọc trạng thái hiện diện"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi trạng thái hiện diện</option>
                      <option value="PRESENT">Có mặt (PRESENT)</option>
                      <option value="NOT_ARRIVED">Chưa đến (NOT_ARRIVED)</option>
                      <option value="LEFT">Tạm rời khu CLS (LEFT)</option>
                    </select>

                    <label className="inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-lg border border-border bg-canvas px-2.5 text-xs font-medium text-navy">
                      <input
                        type="checkbox"
                        checked={formFilters.activeOnly}
                        onChange={(e) => handleFilterChange({ activeOnly: e.target.checked })}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ lượt đang hoạt động</span>
                    </label>

                    <button
                      type="button"
                      onClick={handleResetFilters}
                      data-testid="reset-queue-filters-btn"
                      className="inline-flex h-8 items-center gap-1 rounded-lg border border-border bg-white px-2.5 text-xs font-semibold text-sub transition-colors hover:bg-canvas hover:text-navy"
                    >
                      <RotateCcw className="h-3 w-3" aria-hidden="true" />
                      <span>Đặt lại bộ lọc</span>
                    </button>
                  </div>
                </CardHeader>

                <CardContent className="p-0 px-5 pb-4 pt-2">
                  {boardQuery.isLoading ? (
                    <div
                      role="status"
                      className="rounded-xl border border-border bg-canvas px-4 py-10 text-center text-xs text-sub"
                    >
                      Đang đồng bộ bảng hàng đợi cận lâm sàng...
                    </div>
                  ) : (
                    <DiagnosticQueueManageTable
                      entries={entries}
                      selectedEntryId={activeEntry?.formattedQueueEntryId ?? resolvedEntryKey}
                      hasManagePermission={hasManagePermission}
                      isMutating={isMutating}
                      onSelectEntry={handleSelectEntry}
                      onQuickAction={handleQuickRowAction}
                    />
                  )}
                </CardContent>
              </Card>
            </div>

            {/* Right 5 columns: Queue Entry Detail & Action Panel */}
            <div className="xl:col-span-5">
              <DiagnosticQueueEntryDetailPanel
                entry={activeEntry}
                clinicalContext={detailQuery.data?.clinicalContext ?? null}
                availableServicePoints={
                  detailQuery.data?.availableServicePoints?.length
                    ? detailQuery.data.availableServicePoints
                    : availableServicePoints
                }
                statusHistory={detailQuery.data?.statusHistory ?? []}
                hasManagePermission={hasManagePermission}
                isSubmitting={isMutating}
                onMarkReady={(idOrCode, payload) =>
                  void runMutation(
                    markReadyMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể đánh dấu bệnh nhân sẵn sàng trong hàng đợi.',
                  )
                }
                onCallEntry={(idOrCode, payload) =>
                  void runMutation(
                    callEntryMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể gọi bệnh nhân vào phòng cận lâm sàng.',
                  )
                }
                onStartExecution={(idOrCode, payload) =>
                  void runMutation(
                    startExecutionMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể bắt đầu thực hiện kỹ thuật cận lâm sàng.',
                  )
                }
                onCompleteExecution={(idOrCode, payload) =>
                  void runMutation(
                    completeExecutionMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể hoàn tất thực hiện lượt xếp hàng cận lâm sàng.',
                  )
                }
                onMarkMissedTurn={(idOrCode, payload: DiagnosticQueueTurnActionRequest) =>
                  void runMutation(
                    missedTurnMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể đánh dấu bệnh nhân qua lượt cận lâm sàng.',
                  )
                }
                onRestoreTurn={(idOrCode, payload) =>
                  void runMutation(
                    restoreTurnMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể khôi phục lượt chờ cận lâm sàng.',
                  )
                }
                onCancelEntry={(idOrCode, payload: CancelDiagnosticQueueEntryRequest) =>
                  void runMutation(
                    cancelEntryMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể hủy lượt xếp hàng cận lâm sàng.',
                  )
                }
                onUpdateStatus={(idOrCode, payload: UpdateDiagnosticQueueStatusRequest) =>
                  void runMutation(
                    updateStatusMutation.mutateAsync({ idOrCode, payload }),
                    'Không thể cập nhật trạng thái hàng đợi cận lâm sàng.',
                  )
                }
              />
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
