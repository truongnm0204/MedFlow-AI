import { ArrowLeft, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticQuickActions } from '../components/diagnostic-quick-actions';
import { DiagnosticServicePointSummaryCard } from '../components/diagnostic-service-point-summary-card';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import { DiagnosticWorklistDetailModal } from '../components/diagnostic-worklist-detail-modal';
import { DiagnosticWorklistFilterBar } from '../components/diagnostic-worklist-filter-bar';
import { DiagnosticWorklistStatCards } from '../components/diagnostic-worklist-stat-cards';
import { DiagnosticWorklistTable } from '../components/diagnostic-worklist-table';
import { DIAGNOSTIC_QUICK_ACTIONS_MOCK } from '../data/diagnostic-mock-data';
import {
  DIAGNOSTIC_SERVICE_POINTS_MOCK,
  DIAGNOSTIC_WORKLIST_STATS_MOCK,
} from '../data/diagnostic-worklist-mock';
import {
  useDiagnosticWorklist,
  useDiagnosticWorklistOrderDetail,
} from '../hooks/use-diagnostic-worklist';
import {
  DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS,
  diagnosticWorklistFilterSchema,
  toDiagnosticWorklistQueryParams,
  type DiagnosticWorklistFilterFormValues,
} from '../schemas/diagnostic-worklist-filter.schema';
import type {
  DiagnosticWorklistActionId,
  DiagnosticWorklistItemDto,
} from '../types/diagnostic-worklist.types';

const ACTION_FEEDBACK_LABELS: Record<
  DiagnosticWorklistActionId,
  { title: string; ucCode: string }
> = {
  'confirm-order': {
    title: 'Xác nhận tiếp nhận chỉ định cận lâm sàng',
    ucCode: 'UC39 / UC40',
  },
  'receive-patient': {
    title: 'Tiếp nhận bệnh nhân vào hàng đợi phòng kỹ thuật',
    ucCode: 'UC40',
  },
  'update-execution': {
    title: 'Cập nhật trạng thái thực hiện kỹ thuật cận lâm sàng',
    ucCode: 'UC42',
  },
  'enter-result': {
    title: 'Nhập & trả kết quả cận lâm sàng',
    ucCode: 'UC43',
  },
  'view-result': {
    title: 'Xem phiếu kết quả cận lâm sàng',
    ucCode: 'UC43',
  },
};

const EMPTY_WORKLIST_ITEMS: DiagnosticWorklistItemDto[] = [];
function resolveEffectivePermissions(
  userRoles?: string[],
  staffPermissions?: string[],
): Record<string, true> {
  const lookup: Record<string, true> = {};

  if (Array.isArray(staffPermissions)) {
    for (const perm of staffPermissions) {
      const normalized = perm.trim().toUpperCase();
      if (normalized) {
        lookup[normalized] = true;
      }
    }
    return lookup;
  }

  const normalizedRoles = (userRoles ?? []).map((role) => role.trim().toUpperCase());
  if (
    normalizedRoles.includes('DIAGNOSTIC_STAFF') ||
    normalizedRoles.includes('SYSTEM_ADMIN') ||
    normalizedRoles.includes('HOSPITAL_ADMIN')
  ) {
    lookup.DIAGNOSTIC_ORDER = true;
    lookup.DIAGNOSTIC_RESULT_WRITE = true;
    lookup.QUEUE_MANAGE = true;
  }

  return lookup;
}

export function DiagnosticWorklistPage() {
  const navigate = useNavigate();
  const params = useParams<{ orderIdOrCode?: string }>();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [activeNav, setActiveNav] = useState('orders');
  const [formFilters, setFormFilters] = useState<DiagnosticWorklistFilterFormValues>(
    DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS,
  );
  const [validatedFilters, setValidatedFilters] = useState<DiagnosticWorklistFilterFormValues>(
    DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS,
  );
  const [validationError, setValidationError] = useState<string | null>(null);
  const [manualOrderKey, setManualOrderKey] = useState<string | null>(null);
  const [dismissedRouteOrderKey, setDismissedRouteOrderKey] = useState<string | null>(null);
  const [selectedOrderSnapshot, setSelectedOrderSnapshot] =
    useState<DiagnosticWorklistItemDto | null>(null);
  const [actionBanner, setActionBanner] = useState<string | null>(null);

  const routeOrderKey = params.orderIdOrCode?.trim() || null;
  const selectedOrderKey =
    manualOrderKey ??
    (routeOrderKey && routeOrderKey !== dismissedRouteOrderKey ? routeOrderKey : null);
  const queryParams = useMemo(
    () => toDiagnosticWorklistQueryParams(validatedFilters),
    [validatedFilters],
  );

  const { data, isLoading, isFetching, isError, error, refetch } = useDiagnosticWorklist(
    queryParams,
    true,
  );

  const items = data?.items ?? EMPTY_WORKLIST_ITEMS;
  const stats = data?.stats ?? DIAGNOSTIC_WORKLIST_STATS_MOCK;
  const servicePoints = data?.servicePoints ?? DIAGNOSTIC_SERVICE_POINTS_MOCK;
  const quickActions = data?.quickActions ?? DIAGNOSTIC_QUICK_ACTIONS_MOCK;

  const matchedItemInList = useMemo(() => {
    if (!selectedOrderKey) return null;
    const normalized = selectedOrderKey.trim().toLowerCase();
    return (
      items.find(
        (item) =>
          item.orderCode.toLowerCase() === normalized ||
          item.id.toLowerCase() === normalized ||
          String(item.orderId) === normalized ||
          item.ticketNo.toLowerCase() === normalized,
      ) ?? null
    );
  }, [items, selectedOrderKey]);

  const shouldFetchOrderDetail = Boolean(
    selectedOrderKey && !matchedItemInList && !selectedOrderSnapshot,
  );

  const detailQuery = useDiagnosticWorklistOrderDetail(selectedOrderKey, shouldFetchOrderDetail);

  const activeModalItem = selectedOrderSnapshot ?? matchedItemInList ?? detailQuery.data ?? null;

  const effectivePermissions = useMemo(
    () => resolveEffectivePermissions(user?.roles, data?.staffProfile?.permissions),
    [user?.roles, data?.staffProfile?.permissions],
  );

  const canConfirmOrders = Boolean(
    effectivePermissions.QUEUE_MANAGE ||
    effectivePermissions.DIAGNOSTIC_RESULT_WRITE ||
    effectivePermissions.DIAGNOSTIC_ORDER,
  );
  const canManageQueue = Boolean(effectivePermissions.QUEUE_MANAGE);
  const canWriteResults = Boolean(effectivePermissions.DIAGNOSTIC_RESULT_WRITE);

  const staffDisplayName =
    user?.fullName?.trim() || data?.staffProfile?.fullName?.trim() || 'KTV. Cận lâm sàng';
  const staffCode = user?.staffCode?.trim() || data?.staffProfile?.staffCode?.trim() || null;
  const facilityName =
    data?.facilityName || data?.staffProfile?.hospitalName || 'Bệnh viện MedFlow';
  const departmentName =
    data?.departmentName ||
    data?.staffProfile?.departmentName ||
    'Khoa Xét nghiệm & Chẩn đoán hình ảnh';
  const shiftLabel = data?.shiftLabel || 'Ca sáng • Đồng bộ trực tiếp với phòng khám';

  const handleFilterChange = (patch: Partial<DiagnosticWorklistFilterFormValues>) => {
    const nextFilters = { ...formFilters, ...patch };
    setFormFilters(nextFilters);

    const parsed = diagnosticWorklistFilterSchema.safeParse(nextFilters);
    if (!parsed.success) {
      setValidationError(
        parsed.error.issues[0]?.message ??
          'Tham số lọc danh sách công việc cận lâm sàng không hợp lệ.',
      );
      return;
    }

    setValidationError(null);
    setValidatedFilters(parsed.data);
  };

  const handleResetFilters = () => {
    setFormFilters(DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS);
    setValidatedFilters(DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS);
    setValidationError(null);
    setActionBanner(null);
  };

  const handleSelectOrder = (item: DiagnosticWorklistItemDto) => {
    setSelectedOrderSnapshot(item);
    setManualOrderKey(item.orderCode);
  };

  const handleCloseModal = () => {
    setSelectedOrderSnapshot(null);
    setManualOrderKey(null);
    if (routeOrderKey) {
      setDismissedRouteOrderKey(routeOrderKey);
      navigate('/diagnostic/worklist', { replace: true });
    }
  };

  const handleTriggerAction = (
    item: DiagnosticWorklistItemDto,
    actionId: DiagnosticWorklistActionId,
  ) => {
    if (actionId === 'view-result') {
      navigate(`/diagnostic/results/${encodeURIComponent(item.orderCode)}`);
      return;
    }

    const meta = ACTION_FEEDBACK_LABELS[actionId];
    setSelectedOrderSnapshot(item);
    setManualOrderKey(item.orderCode);
    setActionBanner(
      `Đã mở thao tác "${meta.title}" (${meta.ucCode}) cho phiếu ${item.orderCode} • BN ${item.patientName} (${item.room}).`,
    );
  };

  const handleQuickAction = (actionId: string) => {
    if (actionId === 'confirm-order') {
      handleFilterChange({ status: 'ORDERED', page: 1 });
      setActionBanner('Đang lọc danh sách các chỉ định mới chờ xác nhận (UC39).');
      return;
    }
    if (actionId === 'receive-patient') {
      handleFilterChange({ status: 'CONFIRMED', page: 1 });
      setActionBanner(
        'Đang lọc danh sách bệnh nhân đã xác nhận chỉ định chờ tiếp nhận vào phòng kỹ thuật (UC40).',
      );
      return;
    }
    if (actionId === 'update-execution') {
      handleFilterChange({ status: 'IN_PROGRESS', page: 1 });
      setActionBanner('Đang lọc danh sách ca cận lâm sàng đang thực hiện kỹ thuật (UC42).');
      return;
    }
    if (actionId === 'enter-result') {
      navigate('/diagnostic/results');
    }
  };

  const handleSidebarNav = (navId: string) => {
    setActiveNav(navId);
    if (navId === 'dashboard') {
      navigate('/diagnostic');
    } else if (navId === 'results') {
      navigate('/diagnostic/results');
    }
  };

  const totalPages = data?.totalPages ?? 1;
  const currentPage = data?.page ?? validatedFilters.page;
  const totalCount = data?.totalCount ?? items.length;

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem={activeNav}
        onItemSelect={handleSidebarNav}
        staffName={staffDisplayName}
        departmentName={departmentName}
      />

      <div className="flex min-w-0 flex-1 flex-col">
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
                  Danh sách công việc cận lâm sàng (Diagnostic Worklist)
                </h1>
                <Badge tone="sky">UC39</Badge>
              </div>
              <p className="truncate text-[11px] text-sub">
                {facilityName} • {departmentName} • {shiftLabel}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void refetch()}
              aria-label="Làm mới danh sách công việc CLS"
            >
              <RefreshCw
                className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`}
                aria-hidden="true"
              />
              <span className="hidden sm:inline">Làm mới</span>
            </Button>

            <div className="flex items-center gap-2.5 border-l border-border pl-3">
              <div className="flex h-9 w-9 items-center justify-center rounded-full border border-sky-200 bg-sky-100 text-xs font-bold text-sky-700">
                {staffDisplayName.slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-navy">{staffDisplayName}</div>
                {staffCode && (
                  <div className="font-mono text-[10px] text-sub">Mã NV: {staffCode}</div>
                )}
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-sub transition-colors hover:text-rose-600"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        <main className="flex-1 space-y-6 overflow-y-auto p-6">
          {isError && (
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <span>
                  {getErrorMessage(
                    error,
                    'Không thể tải danh sách công việc cận lâm sàng. Vui lòng thử lại.',
                  )}
                </span>
                <button
                  type="button"
                  onClick={() => void refetch()}
                  className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-50"
                >
                  Thử lại
                </button>
              </div>
            </Alert>
          )}

          {actionBanner && (
            <div role="status" aria-live="polite">
              <Alert tone="info">{actionBanner}</Alert>
            </div>
          )}

          <DiagnosticWorklistStatCards stats={stats} />

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="lg:col-span-8">
              <CardHeader className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <h2 className="text-sm font-bold text-navy">
                      Danh sách chỉ định & hàng đợi cận lâm sàng
                    </h2>
                    <p className="text-[11px] text-sub">
                      Hiển thị {items.length} / {totalCount} phiếu chỉ định • sắp xếp tự động theo
                      trạng thái xử lý và độ ưu tiên lâm sàng (STAT → URGENT → NORMAL)
                    </p>
                  </div>
                  {data?.effectiveDate && (
                    <Badge tone="slate" className="font-mono">
                      Ngày làm việc: {data.effectiveDate}
                    </Badge>
                  )}
                </div>

                <DiagnosticWorklistFilterBar
                  filters={formFilters}
                  validationError={validationError}
                  stats={stats}
                  servicePoints={servicePoints}
                  onFilterChange={handleFilterChange}
                  onReset={handleResetFilters}
                />
              </CardHeader>

              <CardContent className="pt-0 space-y-4">
                {isLoading ? (
                  <div
                    role="status"
                    className="rounded-xl border border-border bg-canvas px-4 py-10 text-center text-xs text-sub"
                  >
                    Đang đồng bộ danh sách chỉ định cận lâm sàng...
                  </div>
                ) : (
                  <DiagnosticWorklistTable
                    items={items}
                    canConfirmOrders={canConfirmOrders}
                    canManageQueue={canManageQueue}
                    canWriteResults={canWriteResults}
                    selectedOrderCode={selectedOrderKey}
                    onSelectOrder={handleSelectOrder}
                    onTriggerAction={handleTriggerAction}
                  />
                )}

                {totalPages > 1 && (
                  <div className="flex items-center justify-between border-t border-border pt-3 text-xs text-sub">
                    <span>
                      Trang <strong className="font-mono text-navy">{currentPage}</strong> /{' '}
                      <strong className="font-mono text-navy">{totalPages}</strong> ({totalCount}{' '}
                      chỉ định)
                    </span>
                    <div className="flex items-center gap-2">
                      <Button
                        variant="secondary"
                        size="sm"
                        disabled={currentPage <= 1}
                        onClick={() => handleFilterChange({ page: Math.max(1, currentPage - 1) })}
                      >
                        <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Trang trước</span>
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        disabled={currentPage >= totalPages}
                        onClick={() =>
                          handleFilterChange({
                            page: Math.min(totalPages, currentPage + 1),
                          })
                        }
                      >
                        <span>Trang sau</span>
                        <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Button>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>

            <div className="space-y-6 lg:col-span-4">
              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div>
                    <h2 className="text-sm font-bold text-navy">
                      Điểm thực hiện CLS (Service Points)
                    </h2>
                    <p className="text-[11px] text-sub">
                      Chọn quầy / phòng để lọc danh sách chỉ định tương ứng
                    </p>
                  </div>
                  <Badge tone="sky">{servicePoints.length} phòng</Badge>
                </CardHeader>
                <CardContent className="pt-0">
                  <DiagnosticServicePointSummaryCard
                    servicePoints={servicePoints}
                    selectedServicePointId={formFilters.servicePointId}
                    onSelectServicePoint={(servicePointId) =>
                      handleFilterChange({ servicePointId, page: 1 })
                    }
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <h2 className="text-sm font-bold text-navy">Lối tắt nghiệp vụ CLS</h2>
                </CardHeader>
                <CardContent className="pt-0">
                  <DiagnosticQuickActions actions={quickActions} onAction={handleQuickAction} />
                </CardContent>
              </Card>
            </div>
          </div>
        </main>
      </div>

      <DiagnosticWorklistDetailModal
        open={Boolean(selectedOrderKey)}
        item={activeModalItem}
        isLoading={detailQuery.isLoading}
        error={detailQuery.error}
        canConfirmOrders={canConfirmOrders}
        canManageQueue={canManageQueue}
        canWriteResults={canWriteResults}
        onClose={handleCloseModal}
        onRetry={() => void detailQuery.refetch()}
        onTriggerAction={handleTriggerAction}
      />
    </div>
  );
}
