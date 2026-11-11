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

import { DiagnosticOrderReviewDetailPanel } from '../components/diagnostic-order-review-detail-panel';
import { DiagnosticOrderReviewStatCards } from '../components/diagnostic-order-review-stat-cards';
import { DiagnosticOrderReviewTable } from '../components/diagnostic-order-review-table';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useConfirmDiagnosticOrder,
  useConfirmDiagnosticOrdersBatch,
  useDiagnosticOrderReviewDetail,
  useDiagnosticOrdersForReview,
} from '../hooks/use-diagnostic-order-review';
import { confirmDiagnosticOrdersBatchSchema } from '../schemas/diagnostic-order-review.schema';
import type {
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrderResponseDto,
  DiagnosticOrderReviewCategoryFilter,
  DiagnosticOrderReviewItemDto,
  DiagnosticOrderReviewPriorityFilter,
  DiagnosticOrderReviewStatusFilter,
} from '../types/diagnostic.types';

const CONFIRM_REQUIRED_PERMISSIONS = [
  'QUEUE_MANAGE',
  'DIAGNOSTIC_RESULT_WRITE',
  'DIAGNOSTIC_ORDER',
];

export function DiagnosticOrderReviewPage() {
  const { orderIdOrCode } = useParams<{ orderIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [keyword, setKeyword] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<DiagnosticOrderReviewCategoryFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<DiagnosticOrderReviewStatusFilter>('ALL');
  const [priorityFilter, setPriorityFilter] = useState<DiagnosticOrderReviewPriorityFilter>('ALL');
  const [pendingOnly, setPendingOnly] = useState(false);

  const [selectedOrderCode, setSelectedOrderCode] = useState<string | null>(null);
  const [selectedBatchIds, setSelectedBatchIds] = useState<number[]>([]);
  const [batchNote, setBatchNote] = useState('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastConfirmationResult, setLastConfirmationResult] =
    useState<ConfirmDiagnosticOrderResponseDto | null>(null);

  const queryParams = useMemo(
    () => ({
      status: statusFilter === 'ALL' ? undefined : statusFilter,
      category: categoryFilter === 'ALL' ? undefined : categoryFilter,
      priority: priorityFilter === 'ALL' ? undefined : priorityFilter,
      pendingOnly: pendingOnly || undefined,
      keyword: keyword.trim() || undefined,
    }),
    [statusFilter, categoryFilter, priorityFilter, pendingOnly, keyword],
  );

  const listQuery = useDiagnosticOrdersForReview(queryParams, true);
  const confirmSingleMutation = useConfirmDiagnosticOrder();
  const confirmBatchMutation = useConfirmDiagnosticOrdersBatch();

  const items = useMemo(() => listQuery.data?.items ?? [], [listQuery.data?.items]);
  const summary = listQuery.data?.summary ?? {
    totalOrders: 0,
    pendingConfirmationCount: 0,
    confirmedCount: 0,
    inProgressCount: 0,
    completedCount: 0,
    cancelledCount: 0,
    urgentOrStatPendingCount: 0,
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
          item.ticketNo?.toUpperCase() === manualOrRouteCode.toUpperCase(),
      );
      if (matched) {
        return matched.orderCode;
      }
      if (orderIdOrCode?.trim() && !selectedOrderCode) {
        return orderIdOrCode.trim();
      }
    }

    const firstPending = items.find((item) => item.status === 'ORDERED') ?? items[0];
    return firstPending.orderCode;
  }, [items, selectedOrderCode, orderIdOrCode]);

  const detailQuery = useDiagnosticOrderReviewDetail(resolvedOrderCode, Boolean(resolvedOrderCode));

  const activeOrder: DiagnosticOrderReviewItemDto | null = useMemo(() => {
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

  const hasConfirmPermission = useMemo(() => {
    const userPerms = user?.permissions;
    const profilePerms = listQuery.data?.staffProfile?.permissions;
    const effectivePerms =
      userPerms && userPerms.length > 0
        ? userPerms
        : (profilePerms ?? CONFIRM_REQUIRED_PERMISSIONS);

    return effectivePerms.some((perm: string) => CONFIRM_REQUIRED_PERMISSIONS.includes(perm));
  }, [user?.permissions, listQuery.data?.staffProfile?.permissions]);

  const handleSelectOrder = (order: DiagnosticOrderReviewItemDto) => {
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

  const handleConfirmSingleOrder = async (
    orderCode: string,
    payload: ConfirmDiagnosticOrderRequest,
  ) => {
    setActionError(null);
    try {
      const response = await confirmSingleMutation.mutateAsync({
        idOrCode: orderCode,
        payload,
      });
      setLastConfirmationResult(response);
      setSelectedOrderCode(response.order.orderCode);
      setSelectedBatchIds((prev) => prev.filter((id) => id !== response.order.orderId));
    } catch (error) {
      setActionError(
        getErrorMessage(
          error,
          'Không thể xác nhận phiếu chỉ định cận lâm sàng. Vui lòng kiểm tra lại.',
        ),
      );
    }
  };

  const handleConfirmBatchOrders = async () => {
    setActionError(null);

    const validation = confirmDiagnosticOrdersBatchSchema.safeParse({
      orderIds: selectedBatchIds,
      isEligibilityVerified: true,
      confirmationNote: batchNote.trim() ? batchNote : null,
      patientPresent: true,
    });

    if (!validation.success) {
      setActionError(
        validation.error.issues[0]?.message ?? 'Danh sách phiếu xác nhận hàng loạt không hợp lệ.',
      );
      return;
    }

    try {
      const response = await confirmBatchMutation.mutateAsync({
        orderIds: selectedBatchIds,
        isEligibilityVerified: true,
        eligibilityConfirmed: true,
        confirmationNote:
          batchNote.trim() ||
          'Đã đối chiếu thông tin hành chính và xác nhận hàng loạt chỉ định cận lâm sàng hợp lệ.',
        patientPresent: true,
      });
      setLastConfirmationResult(response);
      setSelectedBatchIds([]);
      setBatchNote('');
    } catch (error) {
      setActionError(
        getErrorMessage(
          error,
          'Không thể xác nhận hàng loạt phiếu chỉ định cận lâm sàng. Vui lòng thử lại.',
        ),
      );
    }
  };

  const handleSidebarNavigation = (navItem: string) => {
    if (navItem === 'dashboard') {
      navigate('/diagnostic');
    } else if (navItem === 'results') {
      navigate('/diagnostic/results');
    } else if (navItem === 'orders') {
      navigate('/diagnostic/orders');
    } else if (navItem === 'queue') {
      navigate('/diagnostic/queue');
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem="orders"
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
                Kiểm tra & Xác nhận chỉ định cận lâm sàng
              </h1>
              <Badge tone="sky">UC39</Badge>
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
                    'Không thể tải danh sách phiếu chỉ định cận lâm sàng chờ xác nhận.',
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
                data-testid="confirm-action-error-banner"
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

          {/* Confirmation Success Banner */}
          {lastConfirmationResult && (
            <Alert tone="success">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="confirm-order-success-banner"
              >
                <div className="flex items-center gap-2 text-xs">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                  <span>
                    Đã xác nhận thành công <strong>{lastConfirmationResult.confirmedCount}</strong>{' '}
                    phiếu chỉ định. Mã phiếu:{' '}
                    <span className="font-mono font-bold">
                      {lastConfirmationResult.order.orderCode}
                    </span>{' '}
                    • Số thứ tự hàng đợi:{' '}
                    <span className="font-mono font-bold">
                      {lastConfirmationResult.order.ticketNo ?? 'Đã cấp số'}
                    </span>{' '}
                    • Phiên bản trạng thái:{' '}
                    <span className="font-mono">v{lastConfirmationResult.order.stateVersion}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setLastConfirmationResult(null)}
                  className="text-xs font-semibold text-emerald-800 underline hover:text-emerald-950"
                >
                  Ẩn thông báo
                </button>
              </div>
            </Alert>
          )}

          {/* 4 KPI Summary Stat Cards */}
          <DiagnosticOrderReviewStatCards summary={summary} />

          {/* Batch Confirmation Toolbar when 1+ orders selected */}
          {selectedBatchIds.length > 0 && (
            <div
              className="flex flex-col gap-3 rounded-2xl border border-sky-200 bg-sky-50/90 p-4 sm:flex-row sm:items-center sm:justify-between"
              data-testid="batch-confirm-toolbar"
            >
              <div className="flex items-center gap-2 text-xs text-navy">
                <CheckSquare className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>
                  Đang chọn <strong>{selectedBatchIds.length}</strong> phiếu chỉ định đủ điều kiện
                  để xác nhận hàng loạt.
                </span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <input
                  type="text"
                  value={batchNote}
                  onChange={(e) => setBatchNote(e.target.value)}
                  placeholder="Ghi chú xác nhận chung (tùy chọn)..."
                  aria-label="Ghi chú xác nhận hàng loạt"
                  className="h-9 min-w-[240px] rounded-lg border border-border bg-white px-3 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                />
                <Button
                  type="button"
                  size="sm"
                  disabled={!hasConfirmPermission || confirmBatchMutation.isPending}
                  loading={confirmBatchMutation.isPending}
                  onClick={() => void handleConfirmBatchOrders()}
                >
                  <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                  <span>Xác nhận {selectedBatchIds.length} phiếu đã chọn</span>
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
                        Danh sách phiếu chỉ định cận lâm sàng
                      </div>
                      <div className="text-[11px] text-sub">
                        Hiển thị {items.length} phiếu chỉ định • Chọn từng phiếu để đối chiếu lâm
                        sàng & xác nhận
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => void listQuery.refetch()}
                      aria-label="Làm mới danh sách chỉ định"
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
                        aria-label="Tìm kiếm chỉ định chờ xác nhận"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <select
                      value={categoryFilter}
                      onChange={(e) =>
                        setCategoryFilter(e.target.value as DiagnosticOrderReviewCategoryFilter)
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
                        setStatusFilter(e.target.value as DiagnosticOrderReviewStatusFilter)
                      }
                      aria-label="Lọc trạng thái chỉ định"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả trạng thái</option>
                      <option value="ORDERED">Chờ xác nhận (ORDERED)</option>
                      <option value="CONFIRMED">Đã xác nhận (CONFIRMED)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="COMPLETED">Đã hoàn tất (COMPLETED)</option>
                    </select>

                    <select
                      value={priorityFilter}
                      onChange={(e) =>
                        setPriorityFilter(e.target.value as DiagnosticOrderReviewPriorityFilter)
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
                        checked={pendingOnly}
                        onChange={(e) => setPendingOnly(e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ phiếu chờ xác nhận</span>
                    </label>
                  </div>
                </CardHeader>

                <CardContent className="p-0 px-5 pb-4 pt-2">
                  <DiagnosticOrderReviewTable
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

            {/* Right 5 columns: Order Review & Confirmation Detail Panel */}
            <div className="xl:col-span-5">
              <DiagnosticOrderReviewDetailPanel
                order={activeOrder}
                clinicalContext={detailQuery.data?.clinicalContext ?? null}
                availableServicePoints={
                  detailQuery.data?.availableServicePoints?.length
                    ? detailQuery.data.availableServicePoints
                    : availableServicePoints
                }
                hasConfirmPermission={hasConfirmPermission}
                isSubmitting={confirmSingleMutation.isPending}
                onConfirmOrder={(orderCode, payload) =>
                  void handleConfirmSingleOrder(orderCode, payload)
                }
              />
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
