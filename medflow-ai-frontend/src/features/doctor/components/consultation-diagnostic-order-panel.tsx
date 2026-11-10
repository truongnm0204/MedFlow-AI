import {
  CheckCircle2,
  ClipboardPlus,
  FileCheck2,
  FlaskConical,
  Lock,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldAlert,
  Trash2,
} from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import {
  ADDITIONAL_DIAGNOSTIC_REASON_PRESETS,
  buildFollowUpClinicalNoteFromOrder,
  createDiagnosticOrderSchema,
} from '../schemas/consultation-workspace.schema';
import type {
  ConsultationDiagnosticOrderDto,
  CreateDiagnosticOrderRequest,
  DiagnosticOrderPriority,
  DiagnosticServiceCatalogItemDto,
  DiagnosticServiceCategory,
} from '../types/consultation-workspace.types';

export interface DraftDiagnosticOrderItem {
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: DiagnosticServiceCategory | (string & {});
  unitPrice: number;
  roomCode: string | null;
  servicePointName: string | null;
  priority: DiagnosticOrderPriority;
  requiredForConclusion: boolean;
  instruction: string;
}

export interface ConsultationDiagnosticOrderPanelProps {
  existingOrders: ConsultationDiagnosticOrderDto[];
  catalogServices: DiagnosticServiceCatalogItemDto[];
  isCatalogLoading?: boolean;
  catalogError?: string | null;
  onRetryCatalog?: () => void;
  canCreateOrder: boolean;
  hasOrderPermission?: boolean;
  disabledReason?: string | null;
  expectedStateVersion?: number | null;
  isSubmitting?: boolean;
  submitError?: string | null;
  isFormOpen?: boolean;
  onToggleForm?: (open: boolean) => void;
  onSubmitOrder?: (
    payload: CreateDiagnosticOrderRequest,
  ) => Promise<boolean | void> | boolean | void;
  onClearSubmitError?: () => void;
}

const CATEGORY_META: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' }
> = {
  LABORATORY: { label: 'Xét nghiệm', tone: 'sky' },
  IMAGING: { label: 'Chẩn đoán hình ảnh', tone: 'emerald' },
  FUNCTIONAL: { label: 'Thăm dò chức năng', tone: 'amber' },
};

const PRIORITY_META: Record<string, { label: string; tone: 'slate' | 'amber' | 'rose' }> = {
  NORMAL: { label: 'Thường quy (NORMAL)', tone: 'slate' },
  URGENT: { label: 'Khẩn (URGENT)', tone: 'amber' },
  STAT: { label: 'Cấp cứu (STAT)', tone: 'rose' },
};

const ORDER_STATUS_META: Record<
  string,
  { label: string; tone: 'sky' | 'amber' | 'emerald' | 'rose' | 'slate' }
> = {
  ORDERED: { label: 'Đã chỉ định (ORDERED)', tone: 'sky' },
  CONFIRMED: { label: 'Đã tiếp nhận CLS', tone: 'amber' },
  IN_PROGRESS: { label: 'Đang thực hiện CLS', tone: 'amber' },
  COMPLETED: { label: 'Đã có kết quả', tone: 'emerald' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
};

function formatVnd(amount: number): string {
  return `${new Intl.NumberFormat('vi-VN').format(Math.round(amount))} ₫`;
}

export function ConsultationDiagnosticOrderPanel({
  existingOrders,
  catalogServices,
  isCatalogLoading = false,
  catalogError = null,
  onRetryCatalog,
  canCreateOrder,
  hasOrderPermission = true,
  disabledReason = null,
  expectedStateVersion = null,
  isSubmitting = false,
  submitError = null,
  isFormOpen: controlledIsFormOpen,
  onToggleForm,
  onSubmitOrder,
  onClearSubmitError,
}: ConsultationDiagnosticOrderPanelProps) {
  const isOrderable = canCreateOrder && hasOrderPermission;

  const [internalFormOpen, setInternalFormOpen] = useState(false);
  const isFormOpen = controlledIsFormOpen ?? internalFormOpen;

  const setFormOpen = (nextOpen: boolean) => {
    if (onToggleForm) {
      onToggleForm(nextOpen);
    } else {
      setInternalFormOpen(nextOpen);
    }
  };

  const [categoryFilter, setCategoryFilter] = useState<'ALL' | DiagnosticServiceCategory>('ALL');
  const [searchKeyword, setSearchKeyword] = useState('');
  const [selectedItems, setSelectedItems] = useState<DraftDiagnosticOrderItem[]>([]);
  const [clinicalNote, setClinicalNote] = useState('');
  const [transitionToAwaitingResults, setTransitionToAwaitingResults] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const clearErrors = () => {
    if (validationError) setValidationError(null);
    if (submitError && onClearSubmitError) onClearSubmitError();
  };

  const hasExistingOrders = existingOrders.length > 0;
  const completedOrdersCount = existingOrders.filter(
    (o) => o.status === 'COMPLETED' || o.isResultFinalized,
  ).length;
  const pendingOrdersCount = existingOrders.filter(
    (o) => o.status !== 'COMPLETED' && o.status !== 'CANCELLED' && !o.isResultFinalized,
  ).length;

  const activeOrderServiceIds: Record<number, true> = {};
  const completedOrderServiceIds: Record<number, true> = {};
  for (const order of existingOrders) {
    if (order.status !== 'COMPLETED' && order.status !== 'CANCELLED') {
      activeOrderServiceIds[order.serviceId] = true;
    } else if (order.status === 'COMPLETED' || order.isResultFinalized) {
      completedOrderServiceIds[order.serviceId] = true;
    }
  }
  const selectedByServiceId: Record<number, true> = {};
  for (const item of selectedItems) {
    selectedByServiceId[item.serviceId] = true;
  }

  const normalizedQuery = searchKeyword.trim().toLowerCase();
  const filteredCatalog = catalogServices.filter((svc) => {
    if (categoryFilter !== 'ALL' && svc.category !== categoryFilter) {
      return false;
    }
    if (!normalizedQuery) return true;
    return (
      svc.serviceCode.toLowerCase().includes(normalizedQuery) ||
      svc.name.toLowerCase().includes(normalizedQuery) ||
      svc.departmentName.toLowerCase().includes(normalizedQuery) ||
      (svc.roomCode?.toLowerCase().includes(normalizedQuery) ?? false)
    );
  });

  const handleToggleService = (svc: DiagnosticServiceCatalogItemDto) => {
    clearErrors();
    const isAlreadyActive =
      svc.hasActiveOrderInVisit || Boolean(activeOrderServiceIds[svc.serviceId]);
    if (isAlreadyActive || !svc.isDoctorAuthorized) {
      return;
    }

    if (selectedByServiceId[svc.serviceId]) {
      setSelectedItems((prev) => prev.filter((item) => item.serviceId !== svc.serviceId));
      return;
    }

    if (selectedItems.length >= 10) {
      setValidationError('Mỗi lần chỉ định tối đa 10 dịch vụ cận lâm sàng.');
      return;
    }

    setSelectedItems((prev) => [
      ...prev,
      {
        serviceId: svc.serviceId,
        serviceCode: svc.serviceCode,
        serviceName: svc.name,
        category: svc.category,
        unitPrice: svc.unitPrice,
        roomCode: svc.roomCode,
        servicePointName: svc.servicePointName,
        priority: 'NORMAL',
        requiredForConclusion: true,
        instruction: '',
      },
    ]);
  };

  const handleUpdateItemPriority = (serviceId: number, priority: DiagnosticOrderPriority) => {
    clearErrors();
    setSelectedItems((prev) =>
      prev.map((item) => (item.serviceId === serviceId ? { ...item, priority } : item)),
    );
  };

  const handleUpdateItemRequired = (serviceId: number, requiredForConclusion: boolean) => {
    clearErrors();
    setSelectedItems((prev) =>
      prev.map((item) =>
        item.serviceId === serviceId ? { ...item, requiredForConclusion } : item,
      ),
    );
  };

  const handleUpdateItemInstruction = (serviceId: number, instruction: string) => {
    clearErrors();
    setSelectedItems((prev) =>
      prev.map((item) => (item.serviceId === serviceId ? { ...item, instruction } : item)),
    );
  };

  const handleRemoveItem = (serviceId: number) => {
    clearErrors();
    setSelectedItems((prev) => prev.filter((item) => item.serviceId !== serviceId));
  };

  const handleResetForm = () => {
    clearErrors();
    setSelectedItems([]);
    setClinicalNote('');
    setTransitionToAwaitingResults(false);
  };

  const handleStartAdditionalFromOrder = (order: ConsultationDiagnosticOrderDto) => {
    clearErrors();
    setClinicalNote(buildFollowUpClinicalNoteFromOrder(order));
    setFormOpen(true);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setValidationError(null);
    if (onClearSubmitError) onClearSubmitError();

    const candidatePayload: CreateDiagnosticOrderRequest = {
      items: selectedItems.map((item) => ({
        serviceId: item.serviceId,
        serviceCode: item.serviceCode,
        priority: item.priority,
        requiredForConclusion: item.requiredForConclusion,
        instruction: item.instruction.trim() ? item.instruction.trim() : null,
      })),
      clinicalNote: clinicalNote.trim() ? clinicalNote.trim() : null,
      transitionToAwaitingResults,
      expectedStateVersion:
        expectedStateVersion !== null && expectedStateVersion !== undefined
          ? expectedStateVersion
          : undefined,
    };

    const parsed = createDiagnosticOrderSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      setValidationError(
        parsed.error.issues[0]?.message ?? 'Thông tin chỉ định cận lâm sàng không hợp lệ.',
      );
      return;
    }

    if (onSubmitOrder) {
      const succeeded = await onSubmitOrder(candidatePayload);
      if (succeeded !== false) {
        setSelectedItems([]);
        setClinicalNote('');
        setTransitionToAwaitingResults(false);
      }
    }
  };

  const totalEstimatedPrice = selectedItems.reduce((sum, item) => sum + item.unitPrice, 0);

  return (
    <div className="space-y-4 pt-2 border-t border-border">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <FlaskConical className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h4 className="text-xs font-bold text-navy">
              Danh sách Phiếu chỉ định Cận lâm sàng (UC34)
            </h4>
            <p className="text-[11px] text-sub">
              Chỉ định xét nghiệm, chẩn đoán hình ảnh, thăm dò chức năng và cấp số hàng đợi CLS tự
              động
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={existingOrders.length > 0 ? 'sky' : 'slate'}>
            {existingOrders.length} phiếu chỉ định
          </Badge>
          {hasExistingOrders && completedOrdersCount > 0 && (
            <Badge tone="emerald">{completedOrdersCount} đã có KQ</Badge>
          )}
          {hasExistingOrders && pendingOrdersCount > 0 && (
            <Badge tone="amber">{pendingOrdersCount} đang chờ CLS</Badge>
          )}
          {!hasOrderPermission ? (
            <Badge tone="amber">Thiếu quyền DIAGNOSTIC_ORDER</Badge>
          ) : isOrderable ? (
            <Button
              type="button"
              variant={isFormOpen ? 'secondary' : 'primary'}
              size="sm"
              aria-expanded={isFormOpen}
              onClick={() => {
                clearErrors();
                setFormOpen(!isFormOpen);
              }}
            >
              <ClipboardPlus className="h-3.5 w-3.5" aria-hidden="true" />
              <span>
                {isFormOpen
                  ? 'Đóng biểu mẫu UC34'
                  : hasExistingOrders
                    ? 'Chỉ định bổ sung cận lâm sàng (UC34)'
                    : 'Chỉ định cận lâm sàng (UC34)'}
              </span>
            </Button>
          ) : (
            <Badge tone="slate">
              <Lock className="h-3 w-3 mr-1 inline" aria-hidden="true" />
              Khóa chỉ định CLS
            </Badge>
          )}
        </div>
      </div>

      {!isOrderable && (disabledReason || !hasOrderPermission) && (
        <div className="rounded-xl border border-amber-200 bg-amber-50/70 px-3.5 py-2.5 text-xs text-amber-900 flex items-center gap-2">
          <Lock className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <span>
            {!hasOrderPermission
              ? 'Tài khoản chưa được cấp quyền chỉ định cận lâm sàng (DIAGNOSTIC_ORDER).'
              : disabledReason}
          </span>
        </div>
      )}

      {existingOrders.length === 0 ? (
        <div className="rounded-xl bg-canvas border border-border px-4 py-4 text-center text-xs text-sub">
          Chưa có phiếu chỉ định cận lâm sàng nào trong lượt khám này.
        </div>
      ) : (
        <div className="space-y-2.5">
          {existingOrders.map((order) => {
            const catMeta = CATEGORY_META[order.category] ?? {
              label: order.category,
              tone: 'slate' as const,
            };
            const prioMeta = PRIORITY_META[order.priority] ?? {
              label: order.priority,
              tone: 'slate' as const,
            };
            const statusMeta = ORDER_STATUS_META[order.status] ?? {
              label: order.status,
              tone: 'slate' as const,
            };

            return (
              <div
                key={order.orderId}
                className="rounded-xl bg-canvas border border-border p-3.5 space-y-2 text-xs"
              >
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded-md bg-primary/10 text-primary px-2 py-0.5 font-mono font-extrabold">
                      {order.orderCode}
                    </span>
                    {order.ticketNo && (
                      <span className="rounded-md bg-emerald-50 border border-emerald-200 px-2 py-0.5 font-mono font-extrabold text-emerald-800">
                        Số CLS: {order.ticketNo}
                      </span>
                    )}
                    <span className="font-bold text-navy">{order.serviceName}</span>
                    <span className="font-mono text-[11px] text-sub">({order.serviceCode})</span>
                  </div>

                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge tone={catMeta.tone}>{catMeta.label}</Badge>
                    <Badge tone={prioMeta.tone}>{prioMeta.label}</Badge>
                    <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                  </div>
                </div>

                <div className="flex flex-wrap items-center justify-between gap-2 text-[11px] text-sub">
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <span>
                      Phòng thực hiện:{' '}
                      <strong className="font-mono text-navy">
                        {order.roomCode ?? 'Chưa gán'}
                      </strong>
                      {order.servicePointName ? ` • ${order.servicePointName}` : ''}
                    </span>
                    <span>
                      Đơn giá:{' '}
                      <strong className="font-mono text-navy">{formatVnd(order.unitPrice)}</strong>
                    </span>
                    <span>
                      Kết luận:{' '}
                      <strong className="text-navy">
                        {order.requiredForConclusion
                          ? 'Bắt buộc có KQ trước kết luận'
                          : 'Không chặn kết luận'}
                      </strong>
                    </span>
                  </div>
                </div>

                {order.instruction && (
                  <div className="rounded-lg bg-white border border-border px-3 py-1.5 text-[11px] text-ink">
                    <span className="font-semibold text-navy">Chỉ dẫn CLS: </span>
                    <span>{order.instruction}</span>
                  </div>
                )}
                {(order.isResultFinalized ||
                  Boolean(order.resultText) ||
                  Boolean(order.conclusion)) && (
                  <div className="rounded-lg bg-emerald-50/70 border border-emerald-200 px-3 py-2 space-y-1.5 text-[11px] text-ink">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-1.5 font-bold text-emerald-900">
                        <FileCheck2 className="h-3.5 w-3.5 text-emerald-700" aria-hidden="true" />
                        <span>Kết quả cận lâm sàng đã trả</span>
                        {(order.verifiedByStaffName || order.performedByStaffName) && (
                          <span className="font-normal text-sub">
                            • Duyệt bởi: {order.verifiedByStaffName ?? order.performedByStaffName}
                          </span>
                        )}
                      </div>
                      {isOrderable && (
                        <button
                          type="button"
                          onClick={() => handleStartAdditionalFromOrder(order)}
                          aria-label={`Chỉ định bổ sung từ kết quả ${order.serviceCode}`}
                          className="inline-flex items-center gap-1 rounded-lg border border-primary/30 bg-white px-2.5 py-1 text-[11px] font-semibold text-primary hover:bg-primary/5 transition-colors"
                        >
                          <ClipboardPlus className="h-3 w-3" aria-hidden="true" />
                          <span>Chỉ định bổ sung từ KQ này</span>
                        </button>
                      )}
                    </div>
                    {order.resultText && (
                      <div>
                        <span className="font-semibold text-navy">Mô tả / Chỉ số: </span>
                        <span>{order.resultText}</span>
                      </div>
                    )}
                    {order.conclusion && (
                      <div>
                        <span className="font-semibold text-navy">Kết luận CLS: </span>
                        <span className="font-semibold text-emerald-900">{order.conclusion}</span>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {isFormOpen && isOrderable && (
        <form
          onSubmit={(e) => void handleSubmit(e)}
          aria-label="Biểu mẫu chỉ định cận lâm sàng"
          noValidate
          className="rounded-2xl border border-primary/30 bg-sky-50/40 p-4 space-y-4"
        >
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border pb-3">
            <div>
              <h5 className="text-xs font-bold text-navy">
                {hasExistingOrders
                  ? 'Tạo phiếu chỉ định Cận lâm sàng bổ sung (UC34)'
                  : 'Tạo phiếu chỉ định Cận lâm sàng mới (UC34)'}
              </h5>
              <p className="text-[11px] text-sub">
                {hasExistingOrders
                  ? `Ca khám đã có ${existingOrders.length} phiếu chỉ định trước đó. Chọn thêm từ 1 đến 10 dịch vụ cận lâm sàng bổ sung sau khi xem kết quả hoặc diễn biến khám.`
                  : 'Chọn từ 1 đến 10 dịch vụ cận lâm sàng. Mỗi dịch vụ sẽ tự động cấp số hàng đợi CLS tại buồng thực hiện tương ứng.'}
              </p>
            </div>
            {expectedStateVersion !== null && expectedStateVersion !== undefined && (
              <span className="font-mono text-[11px] font-semibold text-sub">
                state_version: v{expectedStateVersion}
              </span>
            )}
          </div>

          <div className="flex flex-col md:flex-row md:items-center justify-between gap-2.5">
            <div
              role="group"
              aria-label="Lọc nhóm dịch vụ cận lâm sàng"
              className="flex flex-wrap items-center gap-1.5"
            >
              {(
                [
                  { id: 'ALL', label: 'Tất cả nhóm' },
                  { id: 'LABORATORY', label: 'Xét nghiệm' },
                  { id: 'IMAGING', label: 'Chẩn đoán hình ảnh' },
                  { id: 'FUNCTIONAL', label: 'Thăm dò chức năng' },
                ] as const
              ).map((tab) => {
                const isSelected = categoryFilter === tab.id;
                return (
                  <button
                    key={tab.id}
                    type="button"
                    aria-pressed={isSelected}
                    onClick={() => setCategoryFilter(tab.id)}
                    className={`rounded-lg px-2.5 py-1 text-xs font-semibold transition-colors ${
                      isSelected
                        ? 'bg-primary text-white'
                        : 'bg-white border border-border text-sub hover:bg-canvas'
                    }`}
                  >
                    {tab.label}
                  </button>
                );
              })}
            </div>

            <div className="relative flex-1 max-w-xs">
              <Search
                className="h-3.5 w-3.5 text-sub absolute left-2.5 top-1/2 -translate-y-1/2"
                aria-hidden="true"
              />
              <input
                type="search"
                value={searchKeyword}
                onChange={(e) => setSearchKeyword(e.target.value)}
                placeholder="Tìm mã hoặc tên dịch vụ CLS..."
                aria-label="Tìm kiếm dịch vụ cận lâm sàng"
                className="w-full rounded-lg border border-border bg-white pl-8 pr-3 py-1.5 text-xs text-ink focus:border-primary focus:outline-none"
              />
            </div>
          </div>

          {catalogError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>{catalogError}</span>
                  {onRetryCatalog && (
                    <button
                      type="button"
                      onClick={onRetryCatalog}
                      className="inline-flex items-center gap-1 rounded-lg bg-rose-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-rose-700"
                    >
                      <RefreshCw className="h-3 w-3" aria-hidden="true" />
                      <span>Tải lại danh mục</span>
                    </button>
                  )}
                </div>
              </Alert>
            </div>
          )}

          {isCatalogLoading ? (
            <div className="rounded-xl bg-white border border-border p-6 flex items-center justify-center gap-2 text-xs text-sub">
              <Spinner className="h-4 w-4 text-primary" />
              <span>Đang tải danh mục dịch vụ cận lâm sàng...</span>
            </div>
          ) : filteredCatalog.length === 0 ? (
            <div className="rounded-xl bg-white border border-border p-4 text-center text-xs text-sub">
              Không tìm thấy dịch vụ cận lâm sàng phù hợp với bộ lọc hiện tại.
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5 max-h-72 overflow-y-auto pr-1">
              {filteredCatalog.map((svc) => {
                const isSelected = Boolean(selectedByServiceId[svc.serviceId]);
                const isAlreadyActive =
                  svc.hasActiveOrderInVisit || Boolean(activeOrderServiceIds[svc.serviceId]);
                const hasCompletedInVisit =
                  !isAlreadyActive && Boolean(completedOrderServiceIds[svc.serviceId]);
                const isUnauthorized = !svc.isDoctorAuthorized;
                const isDisabled = isAlreadyActive || isUnauthorized;
                const catMeta = CATEGORY_META[svc.category] ?? {
                  label: svc.category,
                  tone: 'slate' as const,
                };

                return (
                  <div
                    key={svc.serviceId}
                    className={`rounded-xl border p-3 flex flex-col justify-between gap-2 text-xs transition-colors ${
                      isSelected
                        ? 'border-primary bg-primary/5'
                        : isDisabled
                          ? 'border-border bg-canvas/80 opacity-75'
                          : 'border-border bg-white hover:border-primary/40'
                    }`}
                  >
                    <div className="space-y-1">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="font-mono text-[11px] font-extrabold text-primary">
                            {svc.serviceCode}
                          </span>
                          <Badge tone={catMeta.tone}>{catMeta.label}</Badge>
                        </div>
                        <span className="font-mono font-bold text-navy shrink-0">
                          {formatVnd(svc.unitPrice)}
                        </span>
                      </div>

                      <div className="font-bold text-navy">{svc.name}</div>
                      <div className="text-[11px] text-sub">
                        Phòng:{' '}
                        <span className="font-mono font-semibold">{svc.roomCode ?? 'CLS'}</span>
                        {svc.servicePointName ? ` • ${svc.servicePointName}` : ''}
                      </div>
                    </div>

                    <div className="flex items-center justify-between gap-2 pt-1 border-t border-border/60">
                      {isAlreadyActive ? (
                        <span className="text-[11px] font-semibold text-amber-700 flex items-center gap-1">
                          <Lock className="h-3 w-3" aria-hidden="true" />
                          Đã chỉ định trong ca
                        </span>
                      ) : isUnauthorized ? (
                        <span className="text-[11px] font-semibold text-rose-700 flex items-center gap-1">
                          <ShieldAlert className="h-3 w-3" aria-hidden="true" />
                          Chưa phân quyền BS
                        </span>
                      ) : hasCompletedInVisit ? (
                        <span className="text-[11px] font-semibold text-emerald-700 flex items-center gap-1">
                          <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                          Đã có KQ • Có thể chỉ định lại
                        </span>
                      ) : (
                        <span className="text-[11px] text-sub">
                          {svc.requiresResultVerification ? 'Cần duyệt KQ CLS' : 'Trả KQ trực tiếp'}
                        </span>
                      )}

                      <button
                        type="button"
                        disabled={isDisabled}
                        aria-pressed={isSelected}
                        aria-label={`${isSelected ? 'Bỏ chọn' : 'Chọn'} dịch vụ ${svc.name}`}
                        onClick={() => handleToggleService(svc)}
                        className={`inline-flex items-center gap-1 rounded-lg px-2.5 py-1 text-xs font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
                          isSelected
                            ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                            : isDisabled
                              ? 'bg-canvas text-sub border border-border'
                              : 'bg-primary text-white hover:bg-primary-dark'
                        }`}
                      >
                        {isSelected ? (
                          <>
                            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                            <span>Đã chọn</span>
                          </>
                        ) : (
                          <>
                            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                            <span>Chọn chỉ định</span>
                          </>
                        )}
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          <div className="rounded-xl bg-white border border-border p-3.5 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-bold text-navy">
                Dịch vụ đã chọn trong phiếu ({selectedItems.length}/10)
              </div>
              <div className="text-xs text-sub">
                Tổng tạm tính:{' '}
                <strong className="font-mono text-navy">{formatVnd(totalEstimatedPrice)}</strong>
              </div>
            </div>

            {selectedItems.length === 0 ? (
              <div className="rounded-lg bg-canvas border border-border px-3 py-3 text-center text-xs text-sub">
                Vui lòng chọn ít nhất 1 dịch vụ cận lâm sàng từ danh mục phía trên.
              </div>
            ) : (
              <div className="space-y-3">
                {selectedItems.map((item) => (
                  <div
                    key={item.serviceId}
                    className="rounded-xl bg-canvas border border-border p-3 space-y-2.5 text-xs"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-mono font-extrabold text-primary">
                          {item.serviceCode}
                        </span>
                        <span className="font-bold text-navy">{item.serviceName}</span>
                        <span className="font-mono text-[11px] text-sub">
                          ({item.roomCode ?? 'CLS'} • {formatVnd(item.unitPrice)})
                        </span>
                      </div>
                      <button
                        type="button"
                        onClick={() => handleRemoveItem(item.serviceId)}
                        aria-label={`Bỏ chọn ${item.serviceName}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-rose-200 bg-rose-50 px-2 py-1 text-[11px] font-semibold text-rose-700 hover:bg-rose-100"
                      >
                        <Trash2 className="h-3 w-3" aria-hidden="true" />
                        <span>Xóa</span>
                      </button>
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-12 gap-2.5 items-center">
                      <div className="sm:col-span-4">
                        <label
                          htmlFor={`diag-priority-${item.serviceId}`}
                          className="block text-[11px] font-semibold text-navy mb-1"
                        >
                          Mức độ ưu tiên
                        </label>
                        <select
                          id={`diag-priority-${item.serviceId}`}
                          aria-label={`Mức độ ưu tiên ${item.serviceCode}`}
                          value={item.priority}
                          onChange={(e) =>
                            handleUpdateItemPriority(
                              item.serviceId,
                              e.target.value as DiagnosticOrderPriority,
                            )
                          }
                          className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs text-ink focus:border-primary focus:outline-none"
                        >
                          <option value="NORMAL">Thường quy (NORMAL)</option>
                          <option value="URGENT">Khẩn (URGENT)</option>
                          <option value="STAT">Cấp cứu tối khẩn (STAT)</option>
                        </select>
                      </div>

                      <div className="sm:col-span-8">
                        <label
                          htmlFor={`diag-instruction-${item.serviceId}`}
                          className="block text-[11px] font-semibold text-navy mb-1"
                        >
                          Chỉ dẫn cận lâm sàng / Yêu cầu kỹ thuật
                        </label>
                        <input
                          id={`diag-instruction-${item.serviceId}`}
                          type="text"
                          aria-label={`Chỉ dẫn cận lâm sàng ${item.serviceCode}`}
                          value={item.instruction}
                          onChange={(e) =>
                            handleUpdateItemInstruction(item.serviceId, e.target.value)
                          }
                          placeholder="VD: Nhịn ăn, khảo sát vùng thượng vị, lấy máu ống EDTA..."
                          className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs text-ink focus:border-primary focus:outline-none"
                        />
                      </div>
                    </div>

                    <label className="inline-flex items-center gap-2 text-xs text-ink cursor-pointer">
                      <input
                        type="checkbox"
                        aria-label={`Bắt buộc kết quả ${item.serviceCode}`}
                        checked={item.requiredForConclusion}
                        onChange={(e) => handleUpdateItemRequired(item.serviceId, e.target.checked)}
                        className="h-3.5 w-3.5 rounded border-border text-primary focus:ring-primary"
                      />
                      <span>
                        Bắt buộc có kết quả dịch vụ này trước khi kết luận chẩn đoán & kê đơn
                      </span>
                    </label>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label htmlFor="diag-clinical-note" className="block text-xs font-semibold text-navy">
                Ghi chú lâm sàng / Lý do chỉ định (tối đa 500 ký tự)
              </label>
              <input
                id="diag-clinical-note"
                type="text"
                aria-label="Ghi chú lâm sàng chỉ định"
                value={clinicalNote}
                onChange={(e) => {
                  clearErrors();
                  setClinicalNote(e.target.value);
                }}
                placeholder="VD: Chỉ định bổ sung sau khi xem kết quả ECG, cần loại trừ nhồi máu cơ tim..."
                className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
              />
              <div
                role="group"
                aria-label="Gợi ý lý do chỉ định bổ sung"
                className="flex flex-wrap items-center gap-1.5 pt-0.5"
              >
                {ADDITIONAL_DIAGNOSTIC_REASON_PRESETS.map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    onClick={() => {
                      clearErrors();
                      setClinicalNote(preset);
                    }}
                    className="rounded-md border border-border bg-white px-2 py-0.5 text-[11px] text-sub hover:border-primary/40 hover:text-navy transition-colors"
                  >
                    + {preset}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex items-end">
              <label className="w-full rounded-xl border border-border bg-white p-3 flex items-start gap-2.5 text-xs cursor-pointer">
                <input
                  type="checkbox"
                  aria-label="Chuyển ca khám sang trạng thái Chờ kết quả CLS"
                  checked={transitionToAwaitingResults}
                  onChange={(e) => {
                    clearErrors();
                    setTransitionToAwaitingResults(e.target.checked);
                  }}
                  className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
                />
                <div>
                  <div className="font-bold text-navy">
                    Chuyển ca khám sang Chờ kết quả CLS (AWAITING_RESULTS)
                  </div>
                  <div className="text-[11px] text-sub">
                    Tạm đóng lượt khám hiện tại để bệnh nhân đi thực hiện cận lâm sàng trước khi
                    quay lại phòng khám (UC28).
                  </div>
                </div>
              </label>
            </div>
          </div>

          {(validationError || submitError) && (
            <div role="alert">
              <Alert tone="error">{validationError ?? submitError}</Alert>
            </div>
          )}

          <div className="flex flex-wrap items-center justify-end gap-2 pt-2 border-t border-border">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              disabled={isSubmitting}
              onClick={handleResetForm}
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đặt lại</span>
            </Button>
            <Button
              type="submit"
              variant="primary"
              size="sm"
              loading={isSubmitting}
              disabled={isSubmitting}
            >
              <ClipboardPlus className="h-3.5 w-3.5" aria-hidden="true" />
              <span>
                {hasExistingOrders
                  ? 'Tạo phiếu chỉ định bổ sung CLS (UC34)'
                  : 'Tạo phiếu chỉ định CLS (UC34)'}
              </span>
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}
