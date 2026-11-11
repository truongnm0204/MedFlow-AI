import { CheckCircle2, Filter, RefreshCw, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticResultEntryDetailPanel } from '../components/diagnostic-result-entry-detail-panel';
import { DiagnosticResultEntryStatCards } from '../components/diagnostic-result-entry-stat-cards';
import { DiagnosticResultEntryTable } from '../components/diagnostic-result-entry-table';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useDiagnosticOrdersForResultEntry,
  useDiagnosticResultEntryDetail,
  useEnterDiagnosticResult,
} from '../hooks/use-diagnostic-result-entry';
import type {
  DiagnosticResultEntryCategoryFilter,
  DiagnosticResultEntryItemDto,
  DiagnosticResultEntryPriorityFilter,
  DiagnosticResultEntryQueueStatusFilter,
  DiagnosticResultEntryResultStatusFilter,
  DiagnosticResultEntryStatusFilter,
  EnterDiagnosticResultRequest,
  EnterDiagnosticResultResponseDto,
} from '../types/diagnostic.types';

const RESULT_ENTRY_REQUIRED_PERMISSIONS = ['DIAGNOSTIC_RESULT_WRITE'];

export function DiagnosticResultEntryPage() {
  const { orderIdOrCode } = useParams<{ orderIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [keyword, setKeyword] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<DiagnosticResultEntryCategoryFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<DiagnosticResultEntryStatusFilter>('ALL');
  const [resultStatusFilter, setResultStatusFilter] =
    useState<DiagnosticResultEntryResultStatusFilter>('ALL');
  const [queueStatusFilter, setQueueStatusFilter] =
    useState<DiagnosticResultEntryQueueStatusFilter>('ALL');
  const [priorityFilter, setPriorityFilter] = useState<DiagnosticResultEntryPriorityFilter>('ALL');
  const [readyForResultEntryOnly, setReadyForResultEntryOnly] = useState(false);
  const [pendingFinalizationOnly, setPendingFinalizationOnly] = useState(false);

  const [selectedOrderCode, setSelectedOrderCode] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastEntryResult, setLastEntryResult] = useState<EnterDiagnosticResultResponseDto | null>(
    null,
  );

  const queryParams = useMemo(
    () => ({
      status: statusFilter === 'ALL' ? undefined : statusFilter,
      resultStatus: resultStatusFilter === 'ALL' ? undefined : resultStatusFilter,
      queueStatus: queueStatusFilter === 'ALL' ? undefined : queueStatusFilter,
      category: categoryFilter === 'ALL' ? undefined : categoryFilter,
      priority: priorityFilter === 'ALL' ? undefined : priorityFilter,
      readyForResultEntryOnly: readyForResultEntryOnly || undefined,
      pendingFinalizationOnly: pendingFinalizationOnly || undefined,
      keyword: keyword.trim() || undefined,
    }),
    [
      statusFilter,
      resultStatusFilter,
      queueStatusFilter,
      categoryFilter,
      priorityFilter,
      readyForResultEntryOnly,
      pendingFinalizationOnly,
      keyword,
    ],
  );

  const listQuery = useDiagnosticOrdersForResultEntry(queryParams, true);
  const enterMutation = useEnterDiagnosticResult();

  const items = useMemo(() => listQuery.data?.items ?? [], [listQuery.data?.items]);
  const summary = listQuery.data?.summary ?? {
    totalOrders: 0,
    readyForResultEntryCount: 0,
    inProgressExecutionCount: 0,
    draftOrPreliminaryCount: 0,
    pendingVerificationCount: 0,
    finalizedCount: 0,
    cancelledCount: 0,
    urgentOrStatPendingResultCount: 0,
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

    const firstEligible = items.find((item) => item.canEnterResult) ?? items[0];
    return firstEligible.orderCode;
  }, [items, selectedOrderCode, orderIdOrCode]);

  const detailQuery = useDiagnosticResultEntryDetail(resolvedOrderCode, Boolean(resolvedOrderCode));

  const activeOrder: DiagnosticResultEntryItemDto | null = useMemo(() => {
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

  const hasResultWritePermission = useMemo(() => {
    const userPerms = user?.permissions;
    const profilePerms = listQuery.data?.staffProfile?.permissions;
    const effectivePerms =
      userPerms && userPerms.length > 0
        ? userPerms
        : (profilePerms ?? RESULT_ENTRY_REQUIRED_PERMISSIONS);

    return effectivePerms.some((perm: string) =>
      RESULT_ENTRY_REQUIRED_PERMISSIONS.includes(perm.toUpperCase()),
    );
  }, [user?.permissions, listQuery.data?.staffProfile?.permissions]);

  const handleSelectOrder = (order: DiagnosticResultEntryItemDto) => {
    setSelectedOrderCode(order.orderCode);
    setActionError(null);
  };

  const handleEnterResult = async (orderCode: string, payload: EnterDiagnosticResultRequest) => {
    setActionError(null);
    try {
      const response = await enterMutation.mutateAsync({
        idOrCode: orderCode,
        payload,
      });
      setLastEntryResult(response);
      setSelectedOrderCode(response.order.orderCode);
    } catch (error) {
      setActionError(
        getErrorMessage(error, 'Không thể lưu kết quả cận lâm sàng. Vui lòng kiểm tra lại.'),
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
    } else if (navItem === 'results') {
      navigate('/diagnostic/results');
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem="results"
        onItemSelect={handleSidebarNavigation}
        staffName={staffDisplayName}
        departmentName={departmentName}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top bar */}
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-sm font-bold text-navy">Nhập &amp; trả kết quả cận lâm sàng</h1>
              <Badge tone="sky">UC43</Badge>
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
                    'Không thể tải danh sách phiếu chờ nhập kết quả cận lâm sàng.',
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
                data-testid="result-entry-error-banner"
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

          {/* Result Entry Success Banner */}
          {lastEntryResult && (
            <Alert tone="success">
              <div
                className="flex flex-wrap items-center justify-between gap-3"
                data-testid="result-entry-success"
              >
                <div className="flex items-center gap-2 text-xs">
                  <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                  <span>
                    {lastEntryResult.message} Mã phiếu:{' '}
                    <span className="font-mono font-bold">{lastEntryResult.orderCode}</span> • Phiên
                    bản kết quả:{' '}
                    <span className="font-mono font-bold">v{lastEntryResult.versionNo}</span> •
                    Trạng thái KQ:{' '}
                    <span className="font-mono font-bold">{lastEntryResult.resultStatus}</span> •
                    Kết quả hành động:{' '}
                    <span className="font-mono font-bold">{lastEntryResult.actionOutcome}</span> •
                    Phiên bản trạng thái:{' '}
                    <span className="font-mono">v{lastEntryResult.stateVersion}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setLastEntryResult(null)}
                  className="text-xs font-semibold text-emerald-800 underline hover:text-emerald-950"
                >
                  Ẩn thông báo
                </button>
              </div>
            </Alert>
          )}

          {/* 4 KPI Summary Stat Cards */}
          <DiagnosticResultEntryStatCards summary={summary} />

          {/* Main 12-column Clinical Workbench Grid */}
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
            {/* Left 7 columns: Filter Bar + Orders Table */}
            <div className="xl:col-span-7">
              <Card>
                <CardHeader className="space-y-3 border-b border-border pb-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-bold text-navy">
                        Danh sách phiếu chỉ định chờ nhập &amp; trả kết quả
                      </div>
                      <div className="text-[11px] text-sub">
                        Hiển thị {items.length} phiếu chỉ định • Chọn từng phiếu để xem checklist,
                        lịch sử phiên bản &amp; nhập kết quả
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => void listQuery.refetch()}
                      aria-label="Làm mới danh sách nhập kết quả"
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
                        aria-label="Tìm kiếm chỉ định nhập kết quả"
                        className="h-8 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                      />
                    </div>

                    <select
                      value={resultStatusFilter}
                      onChange={(e) =>
                        setResultStatusFilter(
                          e.target.value as DiagnosticResultEntryResultStatusFilter,
                        )
                      }
                      aria-label="Lọc trạng thái kết quả"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Mọi trạng thái KQ</option>
                      <option value="NONE">Chưa có KQ (NONE)</option>
                      <option value="DRAFT">Bản nháp (DRAFT)</option>
                      <option value="PRELIMINARY">Sơ bộ (PRELIMINARY)</option>
                      <option value="PENDING_VERIFY">Chờ thẩm định (PENDING_VERIFY)</option>
                      <option value="FINALIZED">Đã duyệt (FINALIZED)</option>
                      <option value="VERIFIED">Đã thẩm định (VERIFIED)</option>
                    </select>

                    <select
                      value={categoryFilter}
                      onChange={(e) =>
                        setCategoryFilter(e.target.value as DiagnosticResultEntryCategoryFilter)
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
                        setStatusFilter(e.target.value as DiagnosticResultEntryStatusFilter)
                      }
                      aria-label="Lọc trạng thái chỉ định"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả trạng thái CĐ</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="COMPLETED">Đã hoàn tất (COMPLETED)</option>
                      <option value="CONFIRMED">Đã xác nhận (CONFIRMED)</option>
                      <option value="ORDERED">Chờ xác nhận (ORDERED)</option>
                    </select>

                    <select
                      value={queueStatusFilter}
                      onChange={(e) =>
                        setQueueStatusFilter(
                          e.target.value as DiagnosticResultEntryQueueStatusFilter,
                        )
                      }
                      aria-label="Lọc trạng thái hàng đợi"
                      className="h-8 rounded-lg border border-border bg-canvas px-2 text-xs text-navy focus:border-primary focus:bg-white focus:outline-none"
                    >
                      <option value="ALL">Tất cả hàng đợi</option>
                      <option value="WAITING">Chờ tiếp nhận (WAITING)</option>
                      <option value="READY">Sẵn sàng (READY)</option>
                      <option value="IN_PROGRESS">Đang thực hiện (IN_PROGRESS)</option>
                      <option value="COMPLETED">Hoàn tất (COMPLETED)</option>
                    </select>

                    <select
                      value={priorityFilter}
                      onChange={(e) =>
                        setPriorityFilter(e.target.value as DiagnosticResultEntryPriorityFilter)
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
                        checked={readyForResultEntryOnly}
                        onChange={(e) => setReadyForResultEntryOnly(e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ ca sẵn sàng nhập KQ</span>
                    </label>

                    <label className="inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-lg border border-border bg-canvas px-2.5 text-xs font-medium text-navy">
                      <input
                        type="checkbox"
                        checked={pendingFinalizationOnly}
                        onChange={(e) => setPendingFinalizationOnly(e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <Filter className="h-3 w-3 text-sub" aria-hidden="true" />
                      <span>Chỉ ca chờ duyệt KQ</span>
                    </label>
                  </div>
                </CardHeader>

                <CardContent className="p-0 px-5 pb-4 pt-2">
                  <DiagnosticResultEntryTable
                    items={items}
                    selectedOrderCode={activeOrder?.orderCode ?? resolvedOrderCode}
                    onSelectOrder={handleSelectOrder}
                  />
                </CardContent>
              </Card>
            </div>

            {/* Right 5 columns: Result Entry Detail Panel */}
            <div className="xl:col-span-5">
              <DiagnosticResultEntryDetailPanel
                order={activeOrder}
                clinicalContext={detailQuery.data?.clinicalContext ?? null}
                versions={detailQuery.data?.versions}
                statusHistory={detailQuery.data?.statusHistory}
                hasResultWritePermission={hasResultWritePermission}
                isSubmitting={enterMutation.isPending}
                onEnterResult={(orderCode, payload) => void handleEnterResult(orderCode, payload)}
              />
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
