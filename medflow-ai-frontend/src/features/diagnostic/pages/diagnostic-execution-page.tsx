import { CheckCircle2, CheckSquare, Filter, RefreshCw, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticExecutionDetailPanel } from '../components/diagnostic-execution-detail-panel';
import { DiagnosticExecutionStatCards } from '../components/diagnostic-execution-stat-cards';
import { DiagnosticExecutionTable } from '../components/diagnostic-execution-table';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useCompleteDiagnosticServiceExecution,
  useDiagnosticExecutionDetail,
  useDiagnosticExecutions,
  useStartDiagnosticServiceExecution,
  useUpdateDiagnosticServiceStatus,
  useUpdateDiagnosticServiceStatusBatch,
} from '../hooks/use-diagnostic-execution';
import { updateDiagnosticServiceStatusBatchSchema } from '../schemas/diagnostic-execution.schema';
import type {
  DiagnosticExecutionCategoryFilter,
  DiagnosticExecutionPriorityFilter,
  DiagnosticExecutionQueueStatusFilter,
  DiagnosticExecutionStatusFilter,
  DiagnosticExecutionTargetStatus,
  DiagnosticServiceExecutionItemDto,
  UpdateDiagnosticServiceStatusRequest,
  UpdateDiagnosticServiceStatusResponseDto,
} from '../types/diagnostic.types';

const EXECUTION_REQUIRED_PERMISSIONS = ['QUEUE_MANAGE', 'DIAGNOSTIC_RESULT_WRITE'];

const BATCH_TARGET_OPTIONS: Array<{ value: DiagnosticExecutionTargetStatus; label: string }> = [
  { value: 'IN_PROGRESS', label: 'Bắt đầu thực hiện (IN_PROGRESS)' },
  { value: 'COMPLETED', label: 'Hoàn tất (COMPLETED)' },
  { value: 'CANCELLED', label: 'Hủy thực hiện (CANCELLED)' },
];

export function DiagnosticExecutionPage() {
  const { orderIdOrCode } = useParams<{ orderIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [keyword, setKeyword] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<DiagnosticExecutionCategoryFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<DiagnosticExecutionStatusFilter>('ALL');
  const [queueStatusFilter, setQueueStatusFilter] =
    useState<DiagnosticExecutionQueueStatusFilter>('ALL');
  const [priorityFilter, setPriorityFilter] = useState<DiagnosticExecutionPriorityFilter>('ALL');
  const [readyToExecuteOnly, setReadyToExecuteOnly] = useState(false);

  const [selectedOrderCode, setSelectedOrderCode] = useState<string | null>(null);
  const [selectedBatchIds, setSelectedBatchIds] = useState<number[]>([]);
  const [batchTargetStatus, setBatchTargetStatus] =
    useState<DiagnosticExecutionTargetStatus>('IN_PROGRESS');
  const [batchReason, setBatchReason] = useState('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastUpdateResult, setLastUpdateResult] =
    useState<UpdateDiagnosticServiceStatusResponseDto | null>(null);

  const queryParams = useMemo(
    () => ({
      status: statusFilter === 'ALL' ? undefined : statusFilter,
      queueStatus: queueStatusFilter === 'ALL' ? undefined : queueStatusFilter,
      category: categoryFilter === 'ALL' ? undefined : categoryFilter,
      priority: priorityFilter === 'ALL' ? undefined : priorityFilter,
      readyToExecuteOnly: readyToExecuteOnly || undefined,
      keyword: keyword.trim() || undefined,
    }),
    [statusFilter, queueStatusFilter, categoryFilter, priorityFilter, readyToExecuteOnly, keyword],
  );

  const listQuery = useDiagnosticExecutions(queryParams, true);
  const updateStatusMutation = useUpdateDiagnosticServiceStatus();
  const startMutation = useStartDiagnosticServiceExecution();
  const completeMutation = useCompleteDiagnosticServiceExecution();
  const batchMutation = useUpdateDiagnosticServiceStatusBatch();

  const isBusy =
    updateStatusMutation.isPending ||
    startMutation.isPending ||
    completeMutation.isPending ||
    batchMutation.isPending;

  const items = useMemo(() => listQuery.data?.items ?? [], [listQuery.data?.items]);
  const summary = listQuery.data?.summary ?? {
    totalOrders: 0,
    pendingConfirmationCount: 0,
    waitingExecutionCount: 0,
    readyToStartCount: 0,
    inProgressCount: 0,
    completedCount: 0,
    cancelledCount: 0,
    urgentOrStatCount: 0,
    mandatoryCount: 0,
    laboratoryCount: 0,
    imagingCount: 0,
    functionalCount: 0,
  };

  const resolvedOrderCode = useMemo(() => {
    const manualOrRouteCode = selectedOrderCode ?? orderIdOrCode?.trim() ?? null;
    if (items.length === 0) {
      return orderIdOrCode?.trim() || null;
    }

    if (manualOrRouteCode) {
      const matched = items.find(
        (item) =>
          item.orderCode.toUpperCase() === manualOrRouteCode.toUpperCase() ||
          String(item.orderId) === manualOrRouteCode ||
          item.ticketNo?.toUpperCase() === manualOrRouteCode.toUpperCase() ||
          item.visitCode.toUpperCase() === manualOrRouteCode.toUpperCase(),
      );
      if (matched) {
        return matched.orderCode;
      }
      if (orderIdOrCode?.trim() && !selectedOrderCode) {
        return orderIdOrCode.trim();
      }
    }

    const firstEligible = items.find((item) => item.canUpdateStatus) ?? items[0];
    return firstEligible.orderCode;
  }, [items, selectedOrderCode, orderIdOrCode]);

  const detailQuery = useDiagnosticExecutionDetail(resolvedOrderCode, Boolean(resolvedOrderCode));

  const activeOrder: DiagnosticServiceExecutionItemDto | null = useMemo(() => {
    const fromList = items.find(
      (item) =>
        resolvedOrderCode &&
        (item.orderCode.toUpperCase() === resolvedOrderCode.toUpperCase() ||
          String(item.orderId) === resolvedOrderCode ||
          item.ticketNo?.toUpperCase() === resolvedOrderCode.toUpperCase()),
    );
    if (detailQuery.data?.order) {
      if (
        !fromList ||
        detailQuery.data.order.orderCode === fromList.orderCode ||
        String(detailQuery.data.order.orderId) === resolvedOrderCode
      ) {
        return detailQuery.data.order;
      }
    }
    return fromList ?? null;
  }, [items, resolvedOrderCode, detailQuery.data]);

  const staffDisplayName =
    user?.fullName?.trim() || listQuery.data?.staffProfile?.fullName?.trim() || 'CN. Lê Hoàng Minh';
  const staffCode =
    user?.staffCode?.trim() || listQuery.data?.staffProfile?.staffCode?.trim() || 'DIAG-009';
  const facilityName =
    listQuery.data?.facilityName ||
    listQuery.data?.hospitalName ||
    listQuery.data?.staffProfile?.hospitalName ||
    'Bệnh viện Đại học Y Dược MedFlow';
  const departmentName =
    listQuery.data?.departmentName ||
    listQuery.data?.staffProfile?.departmentName ||
    'Khoa Xét nghiệm & Chẩn đoán hình ảnh';

  const hasUpdatePermission = useMemo(() => {
    const userPerms = user?.permissions;
    const profilePerms = listQuery.data?.staffProfile?.permissions;
    const effectivePerms =
      userPerms && userPerms.length > 0
        ? userPerms
        : (profilePerms ?? EXECUTION_REQUIRED_PERMISSIONS);

    return effectivePerms.some((perm: string) =>
      EXECUTION_REQUIRED_PERMISSIONS.includes(perm.toUpperCase()),
    );
  }, [user?.permissions, listQuery.data?.staffProfile?.permissions]);

  const handleSelectOrder = (order: DiagnosticServiceExecutionItemDto) => {
    setSelectedOrderCode(order.orderCode);
    setActionError(null);
  };

  const handleToggleBatchOrder = (orderId: number) => {
    setSelectedBatchIds((prev) =>
      prev.includes(orderId) ? prev.filter((id) => id !== orderId) : [...prev, orderId],
    );
  };

  const handleToggleAllEligible = (eligibleOrderIds: number[]) => {
    setSelectedBatchIds((prev) => {
      const allSelected =
        eligibleOrderIds.length > 0 && eligibleOrderIds.every((id) => prev.includes(id));
      if (allSelected) {
        return prev.filter((id) => !eligibleOrderIds.includes(id));
      }
      return Array.from(new Set([...prev, ...eligibleOrderIds]));
    });
  };

  const handleSingleUpdate = async (
    orderCode: string,
    payload: UpdateDiagnosticServiceStatusRequest,
    mutation: typeof updateStatusMutation | typeof startMutation | typeof completeMutation,
    fallbackMessage: string,
  ) => {
    setActionError(null);
    setLastUpdateResult(null);
    try {
      const response = await mutation.mutateAsync({ idOrCode: orderCode, payload });
      setLastUpdateResult(response);
      setSelectedOrderCode(response.order.orderCode);
      setSelectedBatchIds((prev) => prev.filter((id) => id !== response.order.orderId));
    } catch (error) {
      setActionError(getErrorMessage(error, fallbackMessage));
    }
  };

  const handleBatchUpdate = async () => {
    setActionError(null);
    setLastUpdateResult(null);

    const validation = updateDiagnosticServiceStatusBatchSchema.safeParse({
      orderIds: selectedBatchIds,
      targetStatus: batchTargetStatus,
      reason: batchReason.trim() || null,
    });

    if (!validation.success) {
      setActionError(
        validation.error.issues[0]?.message ??
          'Dữ liệu cập nhật hàng loạt trạng thái thực hiện không hợp lệ.',
      );
      return;
    }

    try {
      const response = await batchMutation.mutateAsync({
        orderIds: selectedBatchIds,
        targetStatus: batchTargetStatus,
        status: batchTargetStatus,
        reason: batchReason.trim() || null,
      });
      setLastUpdateResult(response);
      setSelectedBatchIds([]);
      setBatchReason('');
    } catch (error) {
      setActionError(
        getErrorMessage(
          error,
          'Không thể cập nhật hàng loạt trạng thái thực hiện phiếu chỉ định. Vui lòng thử lại.',
        ),
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
        activeItem="execution"
        onItemSelect={handleSidebarNavigation}
        staffName={staffDisplayName}
        departmentName={departmentName}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top bar */}
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-sm font-bold text-navy">
                Cập nhật trạng thái thực hiện kỹ thuật cận lâm sàng
              </h1>
              <Badge tone="sky">UC42</Badge>
            </div>
            <div className="text-[11px] text-sub">
              {facilityName} • {departmentName}
            </div>
          </div>

          <div className="flex items-center gap-3">
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
          {/* Load Error Banner */}
          {listQuery.isError && (
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <span>
                  {getErrorMessage(
                    listQuery.error,
                    'Không thể tải danh sách phiếu chỉ định chờ thực hiện kỹ thuật.',
                  )}
                </span>
                <button
                  type="button"
                  onClick={() => void listQuery.refetch()}
                  className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-50"
                >
                  Thử lại
                </button>
              </div>
            </Alert>
          )}

          {/* Action / Mutation Error Banner */}
          {actionError && (
            <Alert tone="error">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="execution-action-error-banner"
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

          {/* Update Success Banner */}
          {lastUpdateResult && (
            <Alert tone="success">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="execution-action-summary"
              >
                <div className="flex items-center gap-2 text-xs">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                  <span>
                    Đã cập nhật <strong>{lastUpdateResult.updatedCount}</strong> phiếu chỉ định •{' '}
                    <span className="font-mono font-bold">{lastUpdateResult.order.orderCode}</span>{' '}
                    • Trạng thái:{' '}
                    <span className="font-mono font-bold">
                      {lastUpdateResult.previousStatus} → {lastUpdateResult.currentStatus}
                    </span>{' '}
                    • Hàng đợi:{' '}
                    <span className="font-mono font-bold">
                      {lastUpdateResult.currentQueueStatus ?? '--'}
                    </span>{' '}
                    • Phiên bản: <span className="font-mono">v{lastUpdateResult.stateVersion}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setLastUpdateResult(null)}
                  className="text-xs font-semibold text-emerald-800 underline hover:text-emerald-950"
                >
                  Ẩn thông báo
                </button>
              </div>
            </Alert>
          )}

          {/* 5 KPI Summary Stat Cards */}
          <DiagnosticExecutionStatCards summary={summary} />

          {/* Batch Update Toolbar */}
          {selectedBatchIds.length > 0 && (
            <div
              className="flex flex-col gap-3 rounded-2xl border border-sky-200 bg-sky-50/90 p-4 sm:flex-row sm:items-center sm:justify-between"
              data-testid="batch-execution-toolbar"
            >
              <div className="flex items-center gap-2 text-xs text-navy">
                <CheckSquare className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>
                  Đang chọn <strong>{selectedBatchIds.length}</strong> phiếu chỉ định đủ điều kiện
                  để cập nhật trạng thái thực hiện hàng loạt.
                </span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <select
                  value={batchTargetStatus}
                  onChange={(e) =>
                    setBatchTargetStatus(e.target.value as DiagnosticExecutionTargetStatus)
                  }
                  aria-label="Chọn trạng thái thực hiện đích cho cập nhật hàng loạt"
                  className="h-9 rounded-lg border border-border bg-white px-2 text-xs text-navy focus:border-primary focus:outline-none"
                >
                  {BATCH_TARGET_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
                <input
                  type="text"
                  value={batchReason}
                  onChange={(e) => setBatchReason(e.target.value)}
                  placeholder={
                    batchTargetStatus === 'CANCELLED'
                      ? 'Lý do hủy (bắt buộc, 5–500 ký tự)...'
                      : 'Lý do / ghi chú (tùy chọn)...'
                  }
                  aria-label="Lý do cập nhật hàng loạt"
                  className="h-9 min-w-[220px] rounded-lg border border-border bg-white px-3 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                />
                <Button
                  type="button"
                  size="sm"
                  disabled={!hasUpdatePermission || batchMutation.isPending}
                  loading={batchMutation.isPending}
                  onClick={() => void handleBatchUpdate()}
                  data-testid="batch-execution-submit"
                >
                  <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                  <span>Cập nhật {selectedBatchIds.length} phiếu đã chọn</span>
                </Button>
                <button
                  type="button"
                  onClick={() => setSelectedBatchIds([])}
                  className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-sub hover:bg-canvas"
                >
                  Bỏ chọn
                </button>
              </div>
            </div>
          )}

          {/* Main 12-column Clinical Workbench Grid */}
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
            {/* Left 7 columns: Filter Bar + Orders Table */}
            <div className="xl:col-span-7">
              <Card>
                <CardHeader className="space-y-3 border-b border-border pb-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-bold text-navy">
                        Danh sách phiếu chỉ định chờ thực hiện kỹ thuật
                      </div>
                      <div className="text-[11px] text-sub">
                        Hiển thị {items.length} phiếu chỉ định • Chọn từng phiếu để bắt đầu / hoàn
                        tất / tạm dừng kỹ thuật
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => void listQuery.refetch()}
                      aria-label="Làm mới danh sách thực hiện"
                      className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
                    >
                      <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Làm mới</span>
                    </button>
                  </div>

                  {/* Filters Row */}
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="relative flex-1 min-w-[200px]">
                      <Search
                        className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-sub"
                        aria-hidden="true"
                      />
                      <input
                        type="search"
                        value={keyword}
                        onChange={(e) => setKeyword(e.target.value)}
                        placeholder="Tìm mã CĐ, số TT, tên/mã BN, dịch vụ..."
                        aria-label="Tìm kiếm phiếu chỉ định thực hiện"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <select
                      value={statusFilter}
                      onChange={(e) =>
                        setStatusFilter(e.target.value as DiagnosticExecutionStatusFilter)
                      }
                      aria-label="Lọc trạng thái thực hiện"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả trạng thái thực hiện</option>
                      <option value="CONFIRMED">Đã xác nhận (CONFIRMED)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="ORDERED">Chờ xác nhận (ORDERED)</option>
                      <option value="COMPLETED">Đã hoàn tất (COMPLETED)</option>
                      <option value="CANCELLED">Đã hủy (CANCELLED)</option>
                    </select>

                    <select
                      value={queueStatusFilter}
                      onChange={(e) =>
                        setQueueStatusFilter(e.target.value as DiagnosticExecutionQueueStatusFilter)
                      }
                      aria-label="Lọc trạng thái hàng đợi"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả hàng đợi</option>
                      <option value="WAITING">Chờ tiếp nhận (WAITING)</option>
                      <option value="READY">Sẵn sàng (READY)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="SKIPPED">Qua lượt (SKIPPED)</option>
                      <option value="COMPLETED">Hoàn tất (COMPLETED)</option>
                    </select>

                    <select
                      value={categoryFilter}
                      onChange={(e) =>
                        setCategoryFilter(e.target.value as DiagnosticExecutionCategoryFilter)
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
                      value={priorityFilter}
                      onChange={(e) =>
                        setPriorityFilter(e.target.value as DiagnosticExecutionPriorityFilter)
                      }
                      aria-label="Lọc mức độ ưu tiên"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi mức ưu tiên</option>
                      <option value="STAT">Cấp tốc (STAT)</option>
                      <option value="URGENT">Khẩn (URGENT)</option>
                      <option value="NORMAL">Bình thường (NORMAL)</option>
                    </select>

                    <label className="inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-lg border border-border bg-canvas px-2.5 text-xs font-medium text-navy">
                      <input
                        type="checkbox"
                        aria-label="Chỉ phiếu sẵn sàng thực hiện"
                        checked={readyToExecuteOnly}
                        onChange={(e) => setReadyToExecuteOnly(e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ phiếu sẵn sàng</span>
                    </label>
                  </div>
                </CardHeader>

                <CardContent className="p-0 px-5 pb-4 pt-2">
                  <DiagnosticExecutionTable
                    items={items}
                    selectedOrderCode={activeOrder?.orderCode ?? resolvedOrderCode}
                    selectedIds={selectedBatchIds}
                    isBusy={isBusy}
                    onSelectOrder={handleSelectOrder}
                    onToggleOrder={handleToggleBatchOrder}
                    onToggleAllEligible={handleToggleAllEligible}
                    onQuickAction={(orderCode, action) => {
                      if (action === 'start') {
                        void handleSingleUpdate(
                          orderCode,
                          { targetStatus: 'IN_PROGRESS', expectedStateVersion: null },
                          startMutation,
                          'Không thể bắt đầu thực hiện phiếu chỉ định. Vui lòng kiểm tra lại.',
                        );
                      } else {
                        void handleSingleUpdate(
                          orderCode,
                          { targetStatus: 'COMPLETED', expectedStateVersion: null },
                          completeMutation,
                          'Không thể hoàn tất thực hiện phiếu chỉ định. Vui lòng kiểm tra lại.',
                        );
                      }
                    }}
                  />
                </CardContent>
              </Card>
            </div>

            {/* Right 5 columns: Execution Detail & History Panel */}
            <div className="xl:col-span-5">
              <DiagnosticExecutionDetailPanel
                order={activeOrder}
                detail={detailQuery.data ?? null}
                clinicalContext={detailQuery.data?.clinicalContext ?? null}
                isLoading={detailQuery.isFetching}
                isBusy={isBusy}
                hasUpdatePermission={hasUpdatePermission}
                onStart={(orderCode, payload) =>
                  void handleSingleUpdate(
                    orderCode,
                    payload,
                    startMutation,
                    'Không thể bắt đầu thực hiện phiếu chỉ định. Vui lòng kiểm tra lại.',
                  )
                }
                onComplete={(orderCode, payload) =>
                  void handleSingleUpdate(
                    orderCode,
                    payload,
                    completeMutation,
                    'Không thể hoàn tất thực hiện phiếu chỉ định. Vui lòng kiểm tra lại.',
                  )
                }
                onUpdateStatus={(orderCode, payload) =>
                  void handleSingleUpdate(
                    orderCode,
                    payload,
                    updateStatusMutation,
                    'Không thể cập nhật trạng thái thực hiện phiếu chỉ định. Vui lòng kiểm tra lại.',
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
