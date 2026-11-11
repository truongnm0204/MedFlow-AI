import { CheckCircle2, CheckSquare, Filter, RefreshCw, Search, UserCheck } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticPatientReceiveDetailPanel } from '../components/diagnostic-patient-receive-detail-panel';
import { DiagnosticPatientReceiveStatCards } from '../components/diagnostic-patient-receive-stat-cards';
import { DiagnosticPatientReceiveTable } from '../components/diagnostic-patient-receive-table';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useDiagnosticOrdersForReceive,
  useDiagnosticPatientReceiveDetail,
  useReceiveDiagnosticPatient,
  useReceiveDiagnosticPatientsBatch,
} from '../hooks/use-diagnostic-patient-receive';
import { receiveDiagnosticPatientsBatchSchema } from '../schemas/diagnostic-patient-receive.schema';
import type {
  DiagnosticPatientReceiveCategoryFilter,
  DiagnosticPatientReceiveItemDto,
  DiagnosticPatientReceivePriorityFilter,
  DiagnosticPatientReceiveQueueStatusFilter,
  DiagnosticPatientReceiveStatusFilter,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientResponseDto,
} from '../types/diagnostic.types';

const RECEIVE_REQUIRED_PERMISSIONS = ['QUEUE_MANAGE'];

export function DiagnosticPatientReceivePage() {
  const { orderIdOrCode } = useParams<{ orderIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [keyword, setKeyword] = useState('');
  const [categoryFilter, setCategoryFilter] =
    useState<DiagnosticPatientReceiveCategoryFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<DiagnosticPatientReceiveStatusFilter>('ALL');
  const [queueStatusFilter, setQueueStatusFilter] =
    useState<DiagnosticPatientReceiveQueueStatusFilter>('ALL');
  const [priorityFilter, setPriorityFilter] =
    useState<DiagnosticPatientReceivePriorityFilter>('ALL');
  const [readyToReceiveOnly, setReadyToReceiveOnly] = useState(false);

  const [selectedOrderCode, setSelectedOrderCode] = useState<string | null>(null);
  const [selectedBatchIds, setSelectedBatchIds] = useState<number[]>([]);
  const [batchNote, setBatchNote] = useState('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastReceiveResult, setLastReceiveResult] =
    useState<ReceiveDiagnosticPatientResponseDto | null>(null);

  const queryParams = useMemo(
    () => ({
      status: statusFilter === 'ALL' ? undefined : statusFilter,
      queueStatus: queueStatusFilter === 'ALL' ? undefined : queueStatusFilter,
      category: categoryFilter === 'ALL' ? undefined : categoryFilter,
      priority: priorityFilter === 'ALL' ? undefined : priorityFilter,
      readyToReceiveOnly: readyToReceiveOnly || undefined,
      keyword: keyword.trim() || undefined,
    }),
    [statusFilter, queueStatusFilter, categoryFilter, priorityFilter, readyToReceiveOnly, keyword],
  );

  const listQuery = useDiagnosticOrdersForReceive(queryParams, true);
  const receiveSingleMutation = useReceiveDiagnosticPatient();
  const receiveBatchMutation = useReceiveDiagnosticPatientsBatch();

  const items = useMemo(() => listQuery.data?.items ?? [], [listQuery.data?.items]);
  const summary = listQuery.data?.summary ?? {
    totalOrders: 0,
    pendingConfirmationCount: 0,
    waitingReceptionCount: 0,
    readyInQueueCount: 0,
    inProgressCount: 0,
    completedCount: 0,
    cancelledCount: 0,
    urgentOrStatWaitingCount: 0,
  };
  const availableServicePoints = listQuery.data?.availableServicePoints ?? [];

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

    const firstEligible = items.find((item) => item.canReceive) ?? items[0];
    return firstEligible.orderCode;
  }, [items, selectedOrderCode, orderIdOrCode]);

  const detailQuery = useDiagnosticPatientReceiveDetail(
    resolvedOrderCode,
    Boolean(resolvedOrderCode),
  );

  const activeOrder: DiagnosticPatientReceiveItemDto | null = useMemo(() => {
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
    listQuery.data?.hospitalName ||
    listQuery.data?.staffProfile?.hospitalName ||
    'Bệnh viện Đại học Y Dược MedFlow';
  const departmentName =
    listQuery.data?.departmentName ||
    listQuery.data?.staffProfile?.departmentName ||
    'Khoa Xét nghiệm & Chẩn đoán hình ảnh';

  const hasReceivePermission = useMemo(() => {
    const userPerms = user?.permissions;
    const profilePerms = listQuery.data?.staffProfile?.permissions;
    const effectivePerms =
      userPerms && userPerms.length > 0
        ? userPerms
        : (profilePerms ?? RECEIVE_REQUIRED_PERMISSIONS);

    return effectivePerms.some((perm: string) =>
      RECEIVE_REQUIRED_PERMISSIONS.includes(perm.toUpperCase()),
    );
  }, [user?.permissions, listQuery.data?.staffProfile?.permissions]);

  const handleSelectOrder = (order: DiagnosticPatientReceiveItemDto) => {
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

  const handleReceiveSinglePatient = async (
    orderCode: string,
    payload: ReceiveDiagnosticPatientRequest,
  ) => {
    setActionError(null);
    try {
      const response = await receiveSingleMutation.mutateAsync({
        idOrCode: orderCode,
        payload,
      });
      setLastReceiveResult(response);
      setSelectedOrderCode(response.order.orderCode);
      setSelectedBatchIds((prev) => prev.filter((id) => id !== response.order.orderId));
    } catch (error) {
      setActionError(
        getErrorMessage(
          error,
          'Không thể tiếp nhận bệnh nhân vào hàng đợi cận lâm sàng. Vui lòng kiểm tra lại.',
        ),
      );
    }
  };

  const handleReceiveBatchPatients = async () => {
    setActionError(null);

    const validation = receiveDiagnosticPatientsBatchSchema.safeParse({
      orderIds: selectedBatchIds,
      isPatientPresent: true,
      isIdentityVerified: true,
      isPreparationVerified: true,
      receptionNote: batchNote.trim() ? batchNote : null,
    });

    if (!validation.success) {
      setActionError(
        validation.error.issues[0]?.message ??
          'Danh sách phiếu tiếp nhận bệnh nhân hàng loạt không hợp lệ.',
      );
      return;
    }

    try {
      const response = await receiveBatchMutation.mutateAsync({
        orderIds: selectedBatchIds,
        isPatientPresent: true,
        patientPresent: true,
        isIdentityVerified: true,
        identityVerified: true,
        isPreparationVerified: true,
        receptionNote:
          batchNote.trim() ||
          'Đã xác nhận bệnh nhân có mặt tại điểm dịch vụ, đối chiếu danh tính và tiếp nhận hàng loạt vào hàng đợi (READY).',
      });
      setLastReceiveResult(response);
      setSelectedBatchIds([]);
      setBatchNote('');
    } catch (error) {
      setActionError(
        getErrorMessage(
          error,
          'Không thể tiếp nhận hàng loạt bệnh nhân cận lâm sàng. Vui lòng thử lại.',
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
        activeItem="queue"
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
                Tiếp nhận bệnh nhân cận lâm sàng tại điểm dịch vụ
              </h1>
              <Badge tone="sky">UC40</Badge>
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
                    'Không thể tải danh sách bệnh nhân chờ tiếp nhận cận lâm sàng.',
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
                data-testid="receive-action-error-banner"
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

          {/* Reception Success Banner */}
          {lastReceiveResult && (
            <Alert tone="success">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="receive-patient-success-banner"
              >
                <div className="flex items-center gap-2 text-xs">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                  <span>
                    Đã tiếp nhận thành công <strong>{lastReceiveResult.receivedCount}</strong> phiếu
                    chỉ định vào hàng đợi kỹ thuật. Mã phiếu:{' '}
                    <span className="font-mono font-bold">{lastReceiveResult.order.orderCode}</span>{' '}
                    • Số thứ tự hàng đợi:{' '}
                    <span className="font-mono font-bold">
                      {lastReceiveResult.order.ticketNo ?? 'Đã cấp số'}
                    </span>{' '}
                    • Trạng thái:{' '}
                    <span className="font-mono font-bold">
                      {lastReceiveResult.order.queueStatus ?? 'READY'}
                    </span>{' '}
                    • Phiên bản trạng thái:{' '}
                    <span className="font-mono">v{lastReceiveResult.order.stateVersion}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setLastReceiveResult(null)}
                  className="text-xs font-semibold text-emerald-800 underline hover:text-emerald-950"
                >
                  Ẩn thông báo
                </button>
              </div>
            </Alert>
          )}

          {/* 4 KPI Summary Stat Cards */}
          <DiagnosticPatientReceiveStatCards summary={summary} />

          {/* Batch Reception Toolbar when 1+ eligible orders selected */}
          {selectedBatchIds.length > 0 && (
            <div
              className="flex flex-col gap-3 rounded-2xl border border-sky-200 bg-sky-50/90 p-4 sm:flex-row sm:items-center sm:justify-between"
              data-testid="batch-receive-toolbar"
            >
              <div className="flex items-center gap-2 text-xs text-navy">
                <CheckSquare className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>
                  Đang chọn <strong>{selectedBatchIds.length}</strong> phiếu chỉ định đủ điều kiện
                  để tiếp nhận bệnh nhân hàng loạt vào hàng đợi.
                </span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <input
                  type="text"
                  value={batchNote}
                  onChange={(e) => setBatchNote(e.target.value)}
                  placeholder="Ghi chú tiếp nhận chung (tùy chọn)..."
                  aria-label="Ghi chú tiếp nhận hàng loạt"
                  className="h-9 min-w-[240px] rounded-lg border border-border bg-white px-3 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                />
                <Button
                  type="button"
                  size="sm"
                  disabled={!hasReceivePermission || receiveBatchMutation.isPending}
                  loading={receiveBatchMutation.isPending}
                  onClick={() => void handleReceiveBatchPatients()}
                >
                  <UserCheck className="h-4 w-4" aria-hidden="true" />
                  <span>Tiếp nhận {selectedBatchIds.length} bệnh nhân đã chọn</span>
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
                        Danh sách bệnh nhân chờ tiếp nhận tại điểm dịch vụ
                      </div>
                      <div className="text-[11px] text-sub">
                        Hiển thị {items.length} phiếu chỉ định • Chọn từng phiếu để xác minh mặt
                        bệnh nhân &amp; chuyển trạng thái READY
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => void listQuery.refetch()}
                      aria-label="Làm mới danh sách tiếp nhận"
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
                        aria-label="Tìm kiếm chỉ định tiếp nhận bệnh nhân"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <select
                      value={categoryFilter}
                      onChange={(e) =>
                        setCategoryFilter(e.target.value as DiagnosticPatientReceiveCategoryFilter)
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
                      value={statusFilter}
                      onChange={(e) =>
                        setStatusFilter(e.target.value as DiagnosticPatientReceiveStatusFilter)
                      }
                      aria-label="Lọc trạng thái chỉ định"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả trạng thái CĐ</option>
                      <option value="CONFIRMED">Đã xác nhận (CONFIRMED)</option>
                      <option value="ORDERED">Chờ xác nhận (ORDERED)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="COMPLETED">Đã hoàn tất (COMPLETED)</option>
                    </select>

                    <select
                      value={queueStatusFilter}
                      onChange={(e) =>
                        setQueueStatusFilter(
                          e.target.value as DiagnosticPatientReceiveQueueStatusFilter,
                        )
                      }
                      aria-label="Lọc trạng thái hàng đợi"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả hàng đợi</option>
                      <option value="WAITING">Chờ tiếp nhận (WAITING)</option>
                      <option value="READY">Sẵn sàng (READY)</option>
                      <option value="SKIPPED">Qua lượt (SKIPPED)</option>
                      <option value="COMPLETED">Hoàn tất (COMPLETED)</option>
                    </select>

                    <select
                      value={priorityFilter}
                      onChange={(e) =>
                        setPriorityFilter(e.target.value as DiagnosticPatientReceivePriorityFilter)
                      }
                      aria-label="Lọc mức độ ưu tiên"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi mức ưu tiên</option>
                      <option value="STAT">Cấp cứu (STAT)</option>
                      <option value="URGENT">Khẩn (URGENT)</option>
                      <option value="NORMAL">Bình thường (NORMAL)</option>
                    </select>

                    <label className="inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-lg border border-border bg-canvas px-2.5 text-xs font-medium text-navy">
                      <input
                        type="checkbox"
                        checked={readyToReceiveOnly}
                        onChange={(e) => setReadyToReceiveOnly(e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ ca chờ tiếp nhận</span>
                    </label>
                  </div>
                </CardHeader>

                <CardContent className="p-0 px-5 pb-4 pt-2">
                  <DiagnosticPatientReceiveTable
                    items={items}
                    selectedOrderCode={activeOrder?.orderCode ?? resolvedOrderCode}
                    selectedBatchIds={selectedBatchIds}
                    onSelectOrder={handleSelectOrder}
                    onToggleBatchOrder={handleToggleBatchOrder}
                    onToggleAllEligible={handleToggleAllEligible}
                  />
                </CardContent>
              </Card>
            </div>

            {/* Right 5 columns: Patient Reception Detail & Verification Panel */}
            <div className="xl:col-span-5">
              <DiagnosticPatientReceiveDetailPanel
                order={activeOrder}
                clinicalContext={detailQuery.data?.clinicalContext ?? null}
                availableServicePoints={
                  detailQuery.data?.availableServicePoints?.length
                    ? detailQuery.data.availableServicePoints
                    : availableServicePoints
                }
                hasReceivePermission={hasReceivePermission}
                isSubmitting={receiveSingleMutation.isPending}
                onReceivePatient={(orderCode, payload) =>
                  void handleReceiveSinglePatient(orderCode, payload)
                }
              />
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
