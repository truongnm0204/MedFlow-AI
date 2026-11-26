import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  PauseCircle,
  Play,
  RefreshCw,
  Search,
  Stethoscope,
  UserCheck,
} from 'lucide-react';
import { type FormEvent, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { formatVnd } from '@/features/payment/utils/payment-format';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';
import { Spinner } from '@/shared/ui/spinner';

import {
  deferReceptionVisitOrOrdersApi,
  fetchAssignableDoctorsForResumeApi,
  fetchReceptionVisitResumeContextApi,
  resumeReceptionCrossDayVisitApi,
} from '../api/visit-cross-day.api';
import type {
  DeferVisitOrOrdersResponseDto,
  ResumeCrossDayVisitResponseDto,
  VisitResumeDiagnosticOrderItemDto,
} from '../types/visit-cross-day.types';

export interface ReceptionVisitResumeModalProps {
  open: boolean;
  initialVisitCode?: string | null;
  onClose: () => void;
  onResumed?: (result: ResumeCrossDayVisitResponseDto) => void;
  onDeferred?: (result: DeferVisitOrOrdersResponseDto) => void;
}

type ModalMode = 'DIAGNOSTIC' | 'RETURN_CONSULTATION' | 'DEFER';

const PRESENCE_BADGE: Record<string, { label: string; tone: 'emerald' | 'amber' | 'slate' }> = {
  PRESENT: { label: 'Có mặt hôm nay', tone: 'emerald' },
  LEFT: { label: 'Đã rời viện', tone: 'slate' },
  DEFERRED: { label: 'Đang tạm hoãn qua ngày', tone: 'amber' },
};

const OVERDUE_REASON_LABELS: Record<string, string> = {
  DIAGNOSTIC_ORDER_OVERDUE_REVIEW:
    'Có chỉ định cận lâm sàng chưa thực hiện đã quá thời hạn hiệu lực — cần xem xét/gia hạn.',
  RETURN_CONSULTATION_OVERDUE_REVIEW:
    'Lượt khám đã quá cửa sổ hẹn quay lại đọc kết quả — cần bác sĩ xem xét khi khám kết luận.',
};

function generateIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID().slice(0, 48);
  }
  return `resume-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function formatLocalDateTime(value: string | null | undefined): string {
  if (!value) return 'Chưa ghi nhận';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function ReceptionVisitResumeModal({
  open,
  initialVisitCode,
  onClose,
  onResumed,
  onDeferred,
}: ReceptionVisitResumeModalProps) {
  const queryClient = useQueryClient();
  const [searchInput, setSearchInput] = useState(initialVisitCode?.trim() ?? '');
  const [activeVisitKey, setActiveVisitKey] = useState(initialVisitCode?.trim() ?? '');
  const [lookupError, setLookupError] = useState<string | null>(null);

  const [mode, setMode] = useState<ModalMode>('DIAGNOSTIC');
  const [selectedOrderById, setSelectedOrderById] = useState<Record<number, boolean>>({});
  const [confirmOverdueOverride, setConfirmOverdueOverride] = useState(false);
  const [resumeNotes, setResumeNotes] = useState('');
  const [selectedReassignDoctorId, setSelectedReassignDoctorId] = useState<string>('');

  const [deferOrderById, setDeferOrderById] = useState<Record<number, boolean>>({});
  const [deferredUntilDate, setDeferredUntilDate] = useState('');
  const [patientLeftHospital, setPatientLeftHospital] = useState(true);
  const [deferReason, setDeferReason] = useState('');
  const [localValidationError, setLocalValidationError] = useState<string | null>(null);

  const idempotencyKeysRef = useRef<Record<string, string>>({});

  const contextQuery = useQuery({
    queryKey: ['reception-visit-resume-context', activeVisitKey],
    queryFn: () => fetchReceptionVisitResumeContextApi(activeVisitKey),
    enabled: open && Boolean(activeVisitKey),
    retry: false,
  });

  const ctx = contextQuery.data ?? null;

  const assignableDoctorsQuery = useQuery({
    queryKey: ['reception-visit-assignable-doctors', activeVisitKey],
    queryFn: () => fetchAssignableDoctorsForResumeApi(activeVisitKey),
    enabled: open && Boolean(activeVisitKey) && mode === 'RETURN_CONSULTATION' && Boolean(ctx),
    retry: false,
  });

  const selectedResumeOrderIds = useMemo<number[]>(() => {
    if (!ctx) return [];
    return ctx.diagnosticOrders
      .filter((order) => {
        const isEligibleOrder =
          order.selectionStatus === 'ACCEPTED' &&
          (order.orderStatus === 'ORDERED' || order.orderStatus === 'CONFIRMED') &&
          order.isPaymentSettled;
        if (!isEligibleOrder) return false;
        const explicit = selectedOrderById[order.orderId];
        return explicit !== undefined
          ? explicit
          : order.canResumeToday && !order.isExpiredForReview;
      })
      .map((order) => order.orderId);
  }, [ctx, selectedOrderById]);

  const hasSelectedExpiredOrder = useMemo(() => {
    if (!ctx || selectedResumeOrderIds.length === 0) return false;
    const selectedSet = new Set(selectedResumeOrderIds);
    return ctx.diagnosticOrders.some((o) => selectedSet.has(o.orderId) && o.isExpiredForReview);
  }, [ctx, selectedResumeOrderIds]);

  const selectedDeferOrderIds = useMemo<number[]>(() => {
    if (!ctx) return [];
    return ctx.diagnosticOrders
      .filter((order) => Boolean(deferOrderById[order.orderId]))
      .map((order) => order.orderId);
  }, [ctx, deferOrderById]);

  const resumeMutation = useMutation({
    mutationFn: async () => {
      if (!ctx) throw new Error('Chưa tải ngữ cảnh lượt khám.');
      const target = mode === 'RETURN_CONSULTATION' ? 'RETURN_CONSULTATION' : 'DIAGNOSTIC';
      const sortedOrders = [...selectedResumeOrderIds].sort((a, b) => a - b);
      const cacheKey = `${ctx.visitCode}:${target}:${sortedOrders.join(',')}:${selectedReassignDoctorId}`;
      if (!idempotencyKeysRef.current[cacheKey]) {
        idempotencyKeysRef.current[cacheKey] = generateIdempotencyKey();
      }

      const reassignIdNum = selectedReassignDoctorId.trim()
        ? Number.parseInt(selectedReassignDoctorId.trim(), 10)
        : null;

      return resumeReceptionCrossDayVisitApi(ctx.visitCode, {
        target,
        diagnosticOrderIds: target === 'DIAGNOSTIC' ? sortedOrders : null,
        reassignDoctorId:
          target === 'RETURN_CONSULTATION' && reassignIdNum && Number.isFinite(reassignIdNum)
            ? reassignIdNum
            : null,
        confirmOverdueOverride: confirmOverdueOverride || null,
        idempotencyKey: idempotencyKeysRef.current[cacheKey],
        notes: resumeNotes.trim() || null,
      });
    },
    onSuccess: (res) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception-visit-resume-context', activeVisitKey],
      });
      onResumed?.(res);
    },
  });

  const deferMutation = useMutation({
    mutationFn: async () => {
      if (!ctx) throw new Error('Chưa tải ngữ cảnh lượt khám.');
      return deferReceptionVisitOrOrdersApi(ctx.visitCode, {
        reason: deferReason.trim(),
        deferredUntilDate: deferredUntilDate.trim() || null,
        diagnosticOrderIds: selectedDeferOrderIds.length > 0 ? selectedDeferOrderIds : null,
        patientLeftHospital,
      });
    },
    onSuccess: (res) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception-visit-resume-context', activeVisitKey],
      });
      onDeferred?.(res);
    },
  });

  const handleLookupSubmit = (e: FormEvent) => {
    e.preventDefault();
    setLookupError(null);
    setLocalValidationError(null);
    const trimmed = searchInput.trim();
    if (!trimmed) {
      setLookupError('Vui lòng nhập mã lượt khám (ví dụ: VIS-202611-0001).');
      return;
    }
    setSelectedOrderById({});
    setDeferOrderById({});
    setActiveVisitKey(trimmed);
  };

  const handleSubmitResume = async () => {
    setLocalValidationError(null);
    if (!ctx) return;

    if (mode === 'DIAGNOSTIC') {
      if (selectedResumeOrderIds.length === 0) {
        setLocalValidationError(
          'Vui lòng chọn ít nhất một chỉ định cận lâm sàng đã thanh toán để tiếp tục thực hiện.',
        );
        return;
      }
      if (hasSelectedExpiredOrder && !confirmOverdueOverride) {
        setLocalValidationError(
          'Chỉ định được chọn đã quá hạn hiệu lực. Vui lòng tích xác nhận gia hạn và nhập ghi chú lý do.',
        );
        return;
      }
      if (hasSelectedExpiredOrder && !resumeNotes.trim()) {
        setLocalValidationError('Vui lòng nhập ghi chú lý do xác nhận tiếp tục chỉ định quá hạn.');
        return;
      }
    } else if (mode === 'RETURN_CONSULTATION') {
      if (!ctx.isReadyForReturn) {
        setLocalValidationError(
          'Lượt khám chưa hoàn tất đầy đủ kết quả cận lâm sàng bắt buộc, chưa thể xếp hàng khám kết luận.',
        );
        return;
      }
      if (ctx.isAssignedDoctorActive === false && !selectedReassignDoctorId.trim()) {
        setLocalValidationError(
          'Bác sĩ phụ trách ban đầu đang nghỉ trực / tạm ngưng. Vui lòng chọn bác sĩ thay thế để khám kết luận.',
        );
        return;
      }
    }

    try {
      await resumeMutation.mutateAsync();
    } catch {
      /* Hiển thị qua resumeMutation.error */
    }
  };

  const handleSubmitDefer = async () => {
    setLocalValidationError(null);
    if (!ctx) return;
    if (deferReason.trim().length < 3) {
      setLocalValidationError('Vui lòng nhập lý do tạm hoãn hoặc rời viện (tối thiểu 3 ký tự).');
      return;
    }

    try {
      await deferMutation.mutateAsync();
    } catch {
      /* Hiển thị qua deferMutation.error */
    }
  };

  const presenceMeta = ctx
    ? (PRESENCE_BADGE[ctx.presenceState?.toUpperCase()] ?? {
        label: ctx.presenceState,
        tone: 'slate' as const,
      })
    : null;

  const activeError =
    localValidationError ??
    (resumeMutation.error ? getErrorMessage(resumeMutation.error) : null) ??
    (deferMutation.error ? getErrorMessage(deferMutation.error) : null);

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <div className="max-h-[88vh] overflow-y-auto p-6 space-y-5 font-sans text-ink">
        <div className="pr-8">
          <div className="flex items-center gap-2">
            <h2 className="text-lg font-bold text-navy">
              Tiếp tục lượt khám & Quản lý hoãn qua ngày
            </h2>
            <Badge tone="sky">Stage C</Badge>
          </div>
          <p className="mt-1 text-xs text-sub">
            Tra cứu lượt khám đang mở từ ngày trước để xếp hàng cận lâm sàng, xếp hàng khám kết luận
            (không thu trùng phí khám), hoặc ghi nhận tạm hoãn / rời viện.
          </p>
        </div>

        <form
          onSubmit={handleLookupSubmit}
          className="flex flex-wrap items-center gap-2 rounded-xl border border-border bg-canvas p-3"
        >
          <div className="relative min-w-[220px] flex-1">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
              aria-hidden="true"
            />
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Nhập mã lượt khám (VD: VIS-202611-0001)"
              aria-label="Mã lượt khám cần tra cứu"
              className="w-full rounded-lg border border-border bg-white py-2 pl-9 pr-3 font-mono text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
          </div>
          <Button type="submit" variant="primary" size="sm" loading={contextQuery.isFetching}>
            <Search className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Tra cứu lượt khám</span>
          </Button>
          {activeVisitKey ? (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => void contextQuery.refetch()}
              aria-label="Tải lại ngữ cảnh lượt khám"
            >
              <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          ) : null}
        </form>

        {lookupError ? <Alert tone="error">{lookupError}</Alert> : null}

        {contextQuery.isPending && activeVisitKey ? (
          <div className="flex items-center justify-center gap-2 py-10 text-xs text-sub">
            <Spinner />
            <span>Đang tải ngữ cảnh lượt khám {activeVisitKey}...</span>
          </div>
        ) : contextQuery.isError ? (
          <Alert tone="error">
            {getErrorMessage(contextQuery.error, 'Không tìm thấy thông tin lượt khám.')}
          </Alert>
        ) : ctx ? (
          <div className="space-y-4">
            <div className="rounded-xl border border-border bg-canvas p-4 space-y-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-sm font-extrabold text-navy">
                      {ctx.visitCode}
                    </span>
                    <Badge tone="sky">{ctx.visitStatus}</Badge>
                    {presenceMeta ? (
                      <Badge tone={presenceMeta.tone}>
                        {ctx.isPresentToday ? 'Đang có mặt hôm nay' : presenceMeta.label}
                      </Badge>
                    ) : null}
                    {ctx.isReadyForReturn ? (
                      <Badge tone="emerald">Đủ điều kiện khám kết luận</Badge>
                    ) : null}
                  </div>
                  <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
                    <span className="inline-flex items-center gap-1">
                      <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
                      Check-in gốc:{' '}
                      <strong className="font-mono text-ink">
                        {formatLocalDateTime(ctx.checkedInAt)}
                      </strong>
                    </span>
                    <span>
                      BS phụ trách:{' '}
                      <strong className="text-ink">
                        {ctx.assignedDoctorName ?? 'Chưa phân công'}
                      </strong>
                      {ctx.isAssignedDoctorActive === false ? (
                        <span className="ml-1.5 font-semibold text-rose-600">(Đang nghỉ trực)</span>
                      ) : null}
                    </span>
                  </div>
                </div>

                <div className="rounded-lg border border-border bg-white px-3 py-2 text-right text-xs">
                  <div className="text-[11px] text-sub">Đã thu / Còn phải thu</div>
                  <div className="font-mono font-bold text-navy">
                    {formatVnd(ctx.chargesSummary.recognizedPaidTotal)} /{' '}
                    <span
                      className={
                        (ctx.chargesSummary.amountDue ?? 0) > 0
                          ? 'text-amber-700'
                          : 'text-emerald-700'
                      }
                    >
                      {formatVnd(ctx.chargesSummary.amountDue ?? 0)}
                    </span>
                  </div>
                </div>
              </div>

              {ctx.isOverdueForReview ? (
                <Alert tone="warning">
                  <div className="space-y-1 text-xs">
                    <div className="flex items-center gap-1.5 font-bold">
                      <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                      <span>Quá hạn — cần xem xét lâm sàng (không tự động hủy ca khám)</span>
                    </div>
                    <ul className="list-disc pl-5">
                      {ctx.overdueReviewReasons.map((reasonCode) => (
                        <li key={reasonCode}>{OVERDUE_REASON_LABELS[reasonCode] ?? reasonCode}</li>
                      ))}
                    </ul>
                  </div>
                </Alert>
              ) : null}

              {ctx.activeTodayQueues.length > 0 ? (
                <div className="flex flex-wrap items-center gap-1.5 text-xs">
                  <span className="font-semibold text-sub">Hàng đợi hôm nay:</span>
                  {ctx.activeTodayQueues.map((q) => (
                    <span
                      key={q.queueEntryId}
                      className="rounded-md border border-emerald-200 bg-emerald-50 px-2 py-0.5 font-mono text-[11px] font-bold text-emerald-800"
                    >
                      {q.ticketNo} ({q.queueType} • {q.status})
                    </span>
                  ))}
                </div>
              ) : null}
            </div>

            <div
              role="tablist"
              aria-label="Chế độ xử lý lượt khám qua ngày"
              className="grid grid-cols-1 gap-2 sm:grid-cols-3"
            >
              <button
                type="button"
                role="tab"
                aria-selected={mode === 'DIAGNOSTIC'}
                onClick={() => {
                  setMode('DIAGNOSTIC');
                  setLocalValidationError(null);
                }}
                className={`flex items-center justify-center gap-1.5 rounded-xl border px-3 py-2.5 text-xs font-bold transition-colors ${
                  mode === 'DIAGNOSTIC'
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-white text-navy hover:bg-canvas'
                }`}
              >
                <Play className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Tiếp tục CLS</span>
              </button>

              <button
                type="button"
                role="tab"
                aria-selected={mode === 'RETURN_CONSULTATION'}
                onClick={() => {
                  setMode('RETURN_CONSULTATION');
                  setLocalValidationError(null);
                }}
                className={`flex items-center justify-center gap-1.5 rounded-xl border px-3 py-2.5 text-xs font-bold transition-colors ${
                  mode === 'RETURN_CONSULTATION'
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-white text-navy hover:bg-canvas'
                }`}
              >
                <Stethoscope className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Khám kết luận</span>
              </button>

              <button
                type="button"
                role="tab"
                aria-selected={mode === 'DEFER'}
                onClick={() => {
                  setMode('DEFER');
                  setLocalValidationError(null);
                }}
                className={`flex items-center justify-center gap-1.5 rounded-xl border px-3 py-2.5 text-xs font-bold transition-colors ${
                  mode === 'DEFER'
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-white text-navy hover:bg-canvas'
                }`}
              >
                <PauseCircle className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Tạm hoãn / Rời viện</span>
              </button>
            </div>

            {activeError ? <Alert tone="error">{activeError}</Alert> : null}

            {resumeMutation.isSuccess && resumeMutation.data ? (
              <Alert tone="success">
                <div className="flex items-center gap-2 text-xs font-semibold">
                  <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
                  <span>
                    Đã xác nhận có mặt và tiếp tục lượt khám {resumeMutation.data.visitCode} (
                    {resumeMutation.data.target}). Số phiếu hôm nay:{' '}
                    <strong className="font-mono">
                      {resumeMutation.data.resumedQueues.map((q) => q.ticketNo).join(', ') || '—'}
                    </strong>
                  </span>
                </div>
              </Alert>
            ) : null}

            {deferMutation.isSuccess && deferMutation.data ? (
              <Alert tone="success">
                <div className="flex items-center gap-2 text-xs font-semibold">
                  <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
                  <span>
                    Đã ghi nhận tạm hoãn / rời viện cho lượt khám {deferMutation.data.visitCode} (
                    trạng thái hiện diện: {deferMutation.data.presenceState}).
                  </span>
                </div>
              </Alert>
            ) : null}

            {mode === 'DIAGNOSTIC' ? (
              <div className="space-y-3 rounded-xl border border-border bg-white p-4">
                <div className="text-xs font-bold text-navy">
                  Danh sách chỉ định cận lâm sàng của lượt khám
                </div>

                {ctx.diagnosticOrders.length === 0 ? (
                  <p className="text-xs text-sub">Lượt khám hiện chưa có chỉ định cận lâm sàng.</p>
                ) : (
                  <div className="divide-y divide-border">
                    {ctx.diagnosticOrders.map((order: VisitResumeDiagnosticOrderItemDto) => {
                      const selectable =
                        order.selectionStatus === 'ACCEPTED' &&
                        (order.orderStatus === 'ORDERED' || order.orderStatus === 'CONFIRMED') &&
                        order.isPaymentSettled;
                      const checked = selectedResumeOrderIds.includes(order.orderId);

                      return (
                        <label
                          key={order.orderId}
                          className={`flex items-start gap-3 py-2.5 text-xs ${
                            selectable ? 'cursor-pointer' : 'opacity-70'
                          }`}
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            disabled={!selectable}
                            onChange={(e) =>
                              setSelectedOrderById((prev) => ({
                                ...prev,
                                [order.orderId]: e.target.checked,
                              }))
                            }
                            aria-label={`Chọn tiếp tục ${order.orderCode}`}
                            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
                          />
                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <span className="font-mono font-bold text-navy">
                                {order.orderCode}
                              </span>
                              <span className="font-semibold text-ink">{order.serviceName}</span>
                              <Badge tone={order.requiredForConclusion ? 'sky' : 'slate'}>
                                {order.requiredForConclusion ? 'Bắt buộc KL' : 'Tùy chọn'}
                              </Badge>
                              <Badge tone={order.isPaymentSettled ? 'emerald' : 'amber'}>
                                {order.isPaymentSettled ? 'Đã thanh toán' : 'Chưa thanh toán'}
                              </Badge>
                              <Badge tone="slate">{order.orderStatus}</Badge>
                              {order.deferredUntilDate ? (
                                <Badge tone="slate">Hẹn ngày {order.deferredUntilDate}</Badge>
                              ) : null}
                              {order.isExpiredForReview ? (
                                <Badge tone="amber">Quá hạn — cần xem xét</Badge>
                              ) : null}
                            </div>
                          </div>
                        </label>
                      );
                    })}
                  </div>
                )}

                {hasSelectedExpiredOrder ? (
                  <div className="rounded-lg border border-amber-200 bg-amber-50/70 p-3 space-y-2 text-xs">
                    <label className="flex items-center gap-2 font-semibold text-amber-900">
                      <input
                        type="checkbox"
                        checked={confirmOverdueOverride}
                        onChange={(e) => setConfirmOverdueOverride(e.target.checked)}
                        className="h-4 w-4 rounded border-amber-400 text-primary"
                      />
                      <span>
                        Xác nhận bác sĩ / quản lý đồng ý gia hạn thực hiện chỉ định quá hạn
                      </span>
                    </label>
                  </div>
                ) : null}

                <div>
                  <label
                    htmlFor="resume-diag-notes"
                    className="block text-xs font-semibold text-navy"
                  >
                    Ghi chú tiếp đón{' '}
                    {hasSelectedExpiredOrder ? '(bắt buộc khi quá hạn)' : '(tùy chọn)'}
                  </label>
                  <textarea
                    id="resume-diag-notes"
                    rows={2}
                    value={resumeNotes}
                    onChange={(e) => setResumeNotes(e.target.value)}
                    placeholder="Ghi chú xác nhận bệnh nhân có mặt thực hiện CLS hôm nay..."
                    className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </div>

                <div className="flex justify-end gap-2 pt-1">
                  <Button type="button" variant="secondary" size="sm" onClick={onClose}>
                    Đóng
                  </Button>
                  <Button
                    type="button"
                    variant="primary"
                    size="sm"
                    loading={resumeMutation.isPending}
                    onClick={() => void handleSubmitResume()}
                  >
                    <UserCheck className="h-4 w-4" aria-hidden="true" />
                    <span>Xác nhận có mặt & Xếp hàng CLS hôm nay</span>
                  </Button>
                </div>
              </div>
            ) : null}

            {mode === 'RETURN_CONSULTATION' ? (
              <div className="space-y-3 rounded-xl border border-border bg-white p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-xs font-bold text-navy">
                    Xếp hàng quay lại khám kết luận (không thu thêm phí khám)
                  </span>
                  <Badge tone={ctx.isReadyForReturn ? 'emerald' : 'amber'}>
                    {ctx.isReadyForReturn
                      ? 'Đã đủ kết quả bắt buộc'
                      : 'Chưa đủ kết quả CLS bắt buộc'}
                  </Badge>
                </div>

                <div>
                  <label
                    htmlFor="resume-reassign-doctor"
                    className="block text-xs font-semibold text-navy"
                  >
                    Chuyển bác sĩ khám kết luận (khi bác sĩ ban đầu nghỉ trực)
                  </label>
                  <select
                    id="resume-reassign-doctor"
                    value={selectedReassignDoctorId}
                    onChange={(e) => setSelectedReassignDoctorId(e.target.value)}
                    className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  >
                    <option value="">
                      Giữ nguyên bác sĩ phụ trách ({ctx.assignedDoctorName ?? 'mặc định'})
                    </option>
                    {(assignableDoctorsQuery.data ?? []).map((doc) => (
                      <option key={doc.doctorId} value={String(doc.doctorId)}>
                        {doc.fullName} ({doc.doctorCode}){doc.roomCode ? ` • ${doc.roomCode}` : ''}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label
                    htmlFor="resume-return-notes"
                    className="block text-xs font-semibold text-navy"
                  >
                    Ghi chú khám kết luận (tùy chọn)
                  </label>
                  <textarea
                    id="resume-return-notes"
                    rows={2}
                    value={resumeNotes}
                    onChange={(e) => setResumeNotes(e.target.value)}
                    placeholder="Ghi chú khi xếp hàng khám kết luận..."
                    className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </div>

                <div className="flex justify-end gap-2 pt-1">
                  <Button type="button" variant="secondary" size="sm" onClick={onClose}>
                    Đóng
                  </Button>
                  <Button
                    type="button"
                    variant="primary"
                    size="sm"
                    loading={resumeMutation.isPending}
                    onClick={() => void handleSubmitResume()}
                  >
                    <Stethoscope className="h-4 w-4" aria-hidden="true" />
                    <span>Xác nhận có mặt & Xếp hàng Khám kết luận</span>
                  </Button>
                </div>
              </div>
            ) : null}

            {mode === 'DEFER' ? (
              <div className="space-y-3 rounded-xl border border-border bg-white p-4">
                <div className="text-xs font-bold text-navy">
                  Ghi nhận tạm hoãn dịch vụ hoặc bệnh nhân rời viện về nhà
                </div>

                {ctx.diagnosticOrders.some(
                  (o) => o.orderStatus === 'ORDERED' || o.orderStatus === 'CONFIRMED',
                ) ? (
                  <div className="space-y-1.5">
                    <div className="text-[11px] font-semibold text-sub">
                      Chỉ định áp dụng ngày hẹn quay lại (để trống nếu hoãn toàn bộ lượt chờ hôm
                      nay):
                    </div>
                    {ctx.diagnosticOrders
                      .filter((o) => o.orderStatus === 'ORDERED' || o.orderStatus === 'CONFIRMED')
                      .map((order) => (
                        <label
                          key={order.orderId}
                          className="flex items-center gap-2 text-xs text-ink"
                        >
                          <input
                            type="checkbox"
                            checked={Boolean(deferOrderById[order.orderId])}
                            onChange={(e) =>
                              setDeferOrderById((prev) => ({
                                ...prev,
                                [order.orderId]: e.target.checked,
                              }))
                            }
                            className="h-4 w-4 rounded border-border text-primary"
                          />
                          <span className="font-mono font-semibold text-navy">
                            {order.orderCode}
                          </span>
                          <span>{order.serviceName}</span>
                        </label>
                      ))}
                  </div>
                ) : null}

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label
                      htmlFor="defer-until-date"
                      className="block text-xs font-semibold text-navy"
                    >
                      Ngày dự kiến quay lại (tùy chọn)
                    </label>
                    <input
                      id="defer-until-date"
                      type="date"
                      value={deferredUntilDate}
                      onChange={(e) => setDeferredUntilDate(e.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 font-mono text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                    />
                  </div>

                  <div className="flex items-end pb-2">
                    <label className="flex items-center gap-2 text-xs font-semibold text-navy">
                      <input
                        type="checkbox"
                        checked={patientLeftHospital}
                        onChange={(e) => setPatientLeftHospital(e.target.checked)}
                        className="h-4 w-4 rounded border-border text-primary"
                      />
                      <span>Bệnh nhân đã rời bệnh viện hôm nay</span>
                    </label>
                  </div>
                </div>

                <div>
                  <label htmlFor="defer-reason" className="block text-xs font-semibold text-navy">
                    Lý do tạm hoãn / rời viện (bắt buộc)
                  </label>
                  <textarea
                    id="defer-reason"
                    rows={2}
                    value={deferReason}
                    onChange={(e) => setDeferReason(e.target.value)}
                    placeholder="Ví dụ: Bệnh nhân cần nhịn ăn sáng mai quay lại xét nghiệm máu..."
                    className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </div>

                <div className="flex justify-end gap-2 pt-1">
                  <Button type="button" variant="secondary" size="sm" onClick={onClose}>
                    Đóng
                  </Button>
                  <Button
                    type="button"
                    variant="primary"
                    size="sm"
                    loading={deferMutation.isPending}
                    onClick={() => void handleSubmitDefer()}
                  >
                    <PauseCircle className="h-4 w-4" aria-hidden="true" />
                    <span>Lưu ghi nhận Tạm hoãn / Rời viện</span>
                  </Button>
                </div>
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}
