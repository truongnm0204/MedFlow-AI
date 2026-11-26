import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ChevronRight,
  CreditCard,
  Home,
  RefreshCw,
  XCircle,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card } from '@/shared/ui/card';

import type { QrPaymentCreateInput } from '../api/payment.api';
import { PaymentNoticeCard } from '../components/payment-notice-card';
import { PaymentPageSkeleton } from '../components/payment-page-skeleton';
import { PaymentPaidCard } from '../components/payment-paid-card';
import { PaymentQrCard } from '../components/payment-qr-card';
import {
  useCreateQrPaymentMutation,
  useDeclineDiagnosticOrderMutation,
  useUpdateDiagnosticSelectionMutation,
} from '../hooks/use-create-qr-payment';
import { useRemainingMs } from '../hooks/use-remaining-ms';
import { useVisitChargesQuery } from '../hooks/use-visit-charges';
import type {
  LineSettlementStatus,
  QrPaymentResponseDto,
  VisitChargeLineDto,
  VisitChargesResponseDto,
} from '../types/payment.types';
import {
  PAYMENT_QR_TTL_MS,
  formatVnd,
  lineSettlementLabel,
  selectionStatusLabel,
} from '../utils/payment-format';

function resolveLineStatus(
  line: VisitChargeLineDto,
  charges: VisitChargesResponseDto,
): LineSettlementStatus {
  if (line.lineSettlementStatus) return line.lineSettlementStatus;
  if (line.chargeStatus === 'PAID_NOT_PERFORMED') return 'PAID_NOT_PERFORMED';
  if (line.chargeStatus === 'CANCELLED_REFUND_PENDING') return 'MISMATCH';
  if (line.chargeStatus === 'CANCELLED') return 'CANCELLED';
  if (line.chargeDataStatus !== 'VALID') return 'UNDETERMINED';
  if (line.payable === false || line.unitPrice === 0) return 'WAIVED';
  if (charges.charges.length === 1) return charges.settlementStatus;
  return 'UNPAID';
}

function isDefaultCheckedLine(line: VisitChargeLineDto, charges: VisitChargesResponseDto): boolean {
  const status = resolveLineStatus(line, charges);
  if (status !== 'UNPAID' || line.chargeId == null || (line.unitPrice ?? 0) <= 0) {
    return false;
  }
  const sel = line.selectionStatus?.toUpperCase();
  if (sel === 'UNDECIDED' || sel === 'DECLINED' || sel === 'REVIEW_REQUIRED') {
    return false;
  }
  return true;
}

export function VisitPaymentPage() {
  const { visitIdOrCode = '' } = useParams<{ visitIdOrCode: string }>();
  const {
    data: charges,
    isPending,
    isError,
    error,
    isFetching,
    refetch,
  } = useVisitChargesQuery(visitIdOrCode);
  const createMutation = useCreateQrPaymentMutation(visitIdOrCode);
  const selectionMutation = useUpdateDiagnosticSelectionMutation(visitIdOrCode);
  const declineMutation = useDeclineDiagnosticOrderMutation(visitIdOrCode);

  const [qr, setQr] = useState<QrPaymentResponseDto | null>(null);
  const [expiresAt, setExpiresAt] = useState<number | null>(null);
  const [manualSelectionByChargeId, setManualSelectionByChargeId] = useState<
    Record<number, boolean>
  >({});
  const [confirmingDeclineOrderId, setConfirmingDeclineOrderId] = useState<number | null>(null);
  const [declineReason, setDeclineReason] = useState('');

  const remainingMs = useRemainingMs(expiresAt);

  const paymentStatus = charges?.paymentStatus?.toUpperCase() ?? '';
  const isPaid = charges?.settlementStatus === 'PAID';
  const isMismatch = charges?.settlementStatus === 'MISMATCH';
  const isClosed =
    !isPaid &&
    !isMismatch &&
    (charges?.charges.length ?? 0) === 0 &&
    (paymentStatus === 'CANCELLED' || paymentStatus === 'FAILED');

  const qrChargeIds = useMemo<number[]>(() => {
    if (!qr) return [];
    if (qr.allocations && qr.allocations.length > 0) {
      return qr.allocations.map((a) => a.visitChargeId);
    }
    return qr.visitChargeId != null ? [qr.visitChargeId] : [];
  }, [qr]);

  const activeQrSettled = useMemo(() => {
    if (!qr || !charges || qrChargeIds.length === 0) return false;
    return qrChargeIds.every((cid) => {
      const found = charges.charges.find((c) => c.chargeId === cid);
      return found != null && resolveLineStatus(found, charges) === 'PAID';
    });
  }, [qr, charges, qrChargeIds]);

  const isExpired =
    (qr === null && paymentStatus === 'EXPIRED' && (charges?.charges.length ?? 0) <= 1) ||
    (expiresAt !== null && remainingMs === 0);

  const hasMultipleCharges = Boolean(charges && charges.charges.length > 1);
  const singleChargeId =
    charges && charges.charges.length === 1
      ? (charges.charges[0]?.chargeId ?? undefined)
      : undefined;

  const selectedChargeIds = useMemo<number[]>(() => {
    if (!charges) return [];
    return charges.charges
      .filter((line) => {
        if (line.chargeId == null) return false;
        const status = resolveLineStatus(line, charges);
        if (status !== 'UNPAID' || (line.unitPrice ?? 0) <= 0) return false;
        const sel = line.selectionStatus?.toUpperCase();
        if (sel === 'DECLINED' || sel === 'SKIPPED_AFTER_PAID' || sel === 'REVIEW_REQUIRED') {
          return false;
        }
        const override = manualSelectionByChargeId[line.chargeId];
        return override !== undefined ? override : isDefaultCheckedLine(line, charges);
      })
      .map((line) => line.chargeId!);
  }, [charges, manualSelectionByChargeId]);

  const selectedTotalVnd = useMemo(() => {
    if (!charges) return 0;
    const set = new Set(selectedChargeIds);
    return charges.charges
      .filter((l) => l.chargeId != null && set.has(l.chargeId))
      .reduce((sum, l) => sum + (l.unitPrice ?? 0), 0);
  }, [charges, selectedChargeIds]);

  const unpaidSelectableCount = useMemo(() => {
    if (!charges) return 0;
    return charges.charges.filter(
      (l) => l.chargeId != null && resolveLineStatus(l, charges) === 'UNPAID',
    ).length;
  }, [charges]);

  const isPayable = Boolean(charges?.canPayByQr);
  const canCreate =
    Boolean(charges) &&
    !isPaid &&
    !isClosed &&
    !isMismatch &&
    (isPayable || isExpired || paymentStatus === 'PENDING');
  const showQr =
    qr !== null && !isPaid && !isClosed && !isExpired && !activeQrSettled && !isMismatch;

  const handleCreate = async (input?: QrPaymentCreateInput) => {
    try {
      const created = await createMutation.mutateAsync(input);
      setQr(created);
      setExpiresAt(Date.now() + PAYMENT_QR_TTL_MS);
    } catch {
      /* Lỗi được hiển thị qua createMutation.error. */
    }
  };

  const handleToggleChargeCheckbox = async (line: VisitChargeLineDto, checked: boolean) => {
    if (line.chargeId == null) return;
    setManualSelectionByChargeId((prev) => ({ ...prev, [line.chargeId!]: checked }));

    if (line.diagnosticOrderId != null && line.requiredForConclusion === false) {
      try {
        await selectionMutation.mutateAsync(
          checked
            ? { acceptedOrderIds: [line.diagnosticOrderId] }
            : { undecidedOrderIds: [line.diagnosticOrderId] },
        );
      } catch {
        /* Giữ chọn cục bộ nếu gọi đồng bộ thất bại; lỗi hiển thị trên đầu trang. */
      }
    }
  };

  const handleConfirmDecline = async (orderId: number) => {
    try {
      await declineMutation.mutateAsync({
        orderIdOrCode: orderId,
        payload: {
          reason: declineReason.trim() || 'Bệnh nhân chọn không thực hiện dịch vụ tùy chọn.',
        },
      });
      setConfirmingDeclineOrderId(null);
      setDeclineReason('');
    } catch {
      /* Lỗi hiển thị qua declineMutation.error. */
    }
  };

  const createLabel = isExpired
    ? 'Tạo mã QR mới'
    : paymentStatus === 'PENDING'
      ? 'Tiếp tục thanh toán'
      : 'Tạo mã QR thanh toán';

  const mutationError = createMutation.error ?? selectionMutation.error ?? declineMutation.error;

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Thanh toán viện phí</span>
          </nav>

          <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h1 className="text-2xl font-extrabold text-ink sm:text-3xl">
                Thanh toán viện phí (VietQR)
              </h1>
              {charges?.visitCode ? (
                <p className="mt-1 text-xs text-sub">
                  Mã lượt khám:{' '}
                  <span className="font-mono font-semibold text-ink">#{charges.visitCode}</span>
                </p>
              ) : null}
            </div>
            <Link
              to="/appointments"
              className="inline-flex items-center gap-1.5 text-xs font-semibold text-primary transition-colors hover:text-primary-dark"
            >
              <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Lịch hẹn của tôi</span>
            </Link>
          </div>

          {isPending && !charges ? (
            <PaymentPageSkeleton />
          ) : isError && !charges ? (
            <div className="space-y-4">
              <Alert tone="error">{getErrorMessage(error)}</Alert>
              <Button variant="primary" size="lg" type="button" onClick={() => void refetch()}>
                <RefreshCw className="h-4 w-4" aria-hidden="true" />
                <span>Thử lại</span>
              </Button>
            </div>
          ) : charges ? (
            <div className="space-y-4">
              {mutationError ? <Alert tone="error">{getErrorMessage(mutationError)}</Alert> : null}

              {charges.readyForReturnAt &&
              !charges.isPresentToday &&
              charges.visitStatus?.toUpperCase() !== 'COMPLETED' &&
              charges.visitStatus?.toUpperCase() !== 'CANCELLED' ? (
                <Alert tone="success">
                  <div className="flex items-center gap-2 text-xs font-semibold">
                    <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>
                      Đủ điều kiện quay lại khám kết luận — vui lòng đến quầy Tiếp đón xác nhận có
                      mặt
                    </span>
                  </div>
                </Alert>
              ) : null}

              {charges.hasReconciliationItems && !isMismatch ? (
                <Alert tone="warning">
                  <div className="flex items-center gap-2 text-xs">
                    <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>
                      <strong>Cần đối soát:</strong> Lượt khám có một giao dịch đang chờ quầy thu
                      ngân đối soát. Các dịch vụ hợp lệ khác bên dưới vẫn có thể thanh toán bình
                      thường.
                    </span>
                  </div>
                </Alert>
              ) : null}

              {isPaid ? (
                <PaymentPaidCard
                  amount={
                    charges.recognizedPaidTotal || charges.paymentAmount || charges.payableTotal
                  }
                  paidAt={charges.paidAt}
                  reference={charges.externalReference}
                />
              ) : isMismatch ? (
                <PaymentNoticeCard
                  tone="warning"
                  title="Cần đối soát thanh toán tại quầy"
                  message="Lượt khám có giao dịch lệch số tiền hoặc phát sinh trên dịch vụ đã hủy. Vui lòng liên hệ quầy thu ngân để đối soát."
                />
              ) : isClosed ? (
                <PaymentNoticeCard
                  tone="warning"
                  title="Giao dịch đã đóng"
                  message="Yêu cầu thanh toán của lượt khám này đã bị hủy hoặc không thành công. Vui lòng liên hệ quầy thu ngân để được hỗ trợ."
                />
              ) : showQr ? (
                <>
                  <PaymentQrCard qr={qr} remainingMs={remainingMs} isExpired={isExpired} />
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <p className="flex items-center gap-1.5 text-xs text-sub" aria-live="polite">
                      <CreditCard className="h-4 w-4 text-primary" aria-hidden="true" />
                      <span>
                        Trạng thái tự cập nhật sau khi chuyển khoản. Bạn không cần tải lại trang.
                      </span>
                    </p>
                    <div className="flex items-center gap-2">
                      {hasMultipleCharges ? (
                        <Button
                          variant="secondary"
                          size="md"
                          type="button"
                          onClick={() => {
                            setQr(null);
                            setExpiresAt(null);
                          }}
                        >
                          Chọn khoản phí khác
                        </Button>
                      ) : null}
                      <Button
                        variant="secondary"
                        size="md"
                        type="button"
                        loading={isFetching}
                        onClick={() => void refetch()}
                      >
                        Kiểm tra lại
                      </Button>
                    </div>
                  </div>
                </>
              ) : hasMultipleCharges ? (
                <Card className="space-y-4 p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-[11px] font-semibold uppercase tracking-wide text-sub">
                        Còn phải thanh toán
                      </p>
                      <p className="mt-1 text-2xl font-extrabold text-ink">
                        {formatVnd(charges.amountDue)}
                      </p>
                      {unpaidSelectableCount > 0 ? (
                        <p className="mt-0.5 text-xs text-sub">
                          Đã chọn {selectedChargeIds.length} dịch vụ:{' '}
                          <span className="font-semibold text-navy">
                            {formatVnd(selectedTotalVnd)}
                          </span>
                        </p>
                      ) : null}
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge tone="amber">Thanh toán linh hoạt (đơn lẻ hoặc gộp)</Badge>
                      {selectedChargeIds.length >= 2 ? (
                        <Button
                          variant="primary"
                          size="sm"
                          type="button"
                          loading={createMutation.isPending}
                          onClick={() => void handleCreate({ visitChargeIds: selectedChargeIds })}
                        >
                          <CreditCard className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Thanh toán các dịch vụ đã chọn ({selectedChargeIds.length})</span>
                        </Button>
                      ) : null}
                    </div>
                  </div>

                  {charges.activePendingQrPaymentCode &&
                  charges.activePendingQrChargeIds &&
                  charges.activePendingQrChargeIds.length > 0 ? (
                    <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-amber-200 bg-amber-50/70 px-4 py-2.5 text-xs text-amber-900">
                      <span>
                        Đang có mã QR chờ thanh toán{' '}
                        <strong className="font-mono">{charges.activePendingQrPaymentCode}</strong>{' '}
                        ({formatVnd(charges.activePendingQrAmount ?? null)}).
                      </span>
                      <Button
                        variant="secondary"
                        size="sm"
                        type="button"
                        loading={createMutation.isPending}
                        onClick={() =>
                          void handleCreate({
                            visitChargeIds: charges.activePendingQrChargeIds ?? [],
                          })
                        }
                      >
                        Mở lại mã QR đang chờ
                      </Button>
                    </div>
                  ) : null}

                  <div className="divide-y divide-border rounded-xl border border-border bg-white">
                    {charges.charges.map((line, idx) => {
                      const status = resolveLineStatus(line, charges);
                      const linePending = line.paymentStatus?.toUpperCase() === 'PENDING';
                      const lineExpired = line.paymentStatus?.toUpperCase() === 'EXPIRED';
                      const selUpper = line.selectionStatus?.toUpperCase() ?? null;
                      const lineCanCreate =
                        status === 'UNPAID' &&
                        selUpper !== 'REVIEW_REQUIRED' &&
                        selUpper !== 'DECLINED' &&
                        (Boolean(line.canPayByQr) || linePending || lineExpired);
                      const isOptional =
                        line.chargeType === 'DIAGNOSTIC' && line.requiredForConclusion === false;
                      const isMandatory =
                        line.chargeType === 'DIAGNOSTIC' && line.requiredForConclusion !== false;
                      const isChecked =
                        line.chargeId != null && selectedChargeIds.includes(line.chargeId);
                      const canDeclineUnpaidOptional =
                        isOptional &&
                        status === 'UNPAID' &&
                        line.diagnosticOrderId != null &&
                        (line.canDecline ?? true);

                      return (
                        <div key={line.chargeId ?? `chg-${idx}`} className="space-y-2 p-4 text-xs">
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <div className="flex items-start gap-3">
                              {status === 'UNPAID' &&
                              line.chargeId != null &&
                              (line.unitPrice ?? 0) > 0 &&
                              selUpper !== 'REVIEW_REQUIRED' ? (
                                <input
                                  type="checkbox"
                                  aria-label={`Chọn thanh toán ${line.serviceName ?? 'khoản phí'}`}
                                  checked={isChecked}
                                  onChange={(e) =>
                                    void handleToggleChargeCheckbox(line, e.target.checked)
                                  }
                                  className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
                                />
                              ) : null}

                              <div className="space-y-1">
                                <div className="flex flex-wrap items-center gap-1.5">
                                  <span className="font-bold text-ink">
                                    {line.serviceName ?? 'Khoản phí dịch vụ'}
                                  </span>
                                  {isMandatory ? <Badge tone="sky">Bắt buộc</Badge> : null}
                                  {isOptional ? <Badge tone="slate">Tùy chọn</Badge> : null}
                                  <Badge
                                    tone={
                                      status === 'PAID' || status === 'WAIVED'
                                        ? 'emerald'
                                        : status === 'UNPAID'
                                          ? 'amber'
                                          : status === 'PAID_NOT_PERFORMED'
                                            ? 'slate'
                                            : 'rose'
                                    }
                                  >
                                    {lineSettlementLabel(status)}
                                  </Badge>
                                  {selUpper &&
                                  selUpper !== 'ACCEPTED' &&
                                  status !== 'PAID_NOT_PERFORMED' ? (
                                    <Badge tone={selUpper === 'REVIEW_REQUIRED' ? 'rose' : 'slate'}>
                                      {selectionStatusLabel(selUpper)}
                                    </Badge>
                                  ) : null}
                                </div>

                                <p className="text-sub">
                                  Số tiền:{' '}
                                  <span className="font-semibold text-ink">
                                    {formatVnd(line.unitPrice)}
                                  </span>
                                  {line.paymentCode ? (
                                    <span className="ml-2 font-mono text-[11px]">
                                      • Mã: {line.paymentCode}
                                    </span>
                                  ) : null}
                                </p>
                              </div>
                            </div>

                            <div className="flex flex-wrap items-center gap-2">
                              {canDeclineUnpaidOptional && line.diagnosticOrderId != null ? (
                                <Button
                                  variant="secondary"
                                  size="sm"
                                  type="button"
                                  onClick={() => {
                                    setConfirmingDeclineOrderId(
                                      confirmingDeclineOrderId === line.diagnosticOrderId
                                        ? null
                                        : line.diagnosticOrderId,
                                    );
                                    setDeclineReason('');
                                  }}
                                >
                                  <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                                  <span>Không thực hiện</span>
                                </Button>
                              ) : null}

                              {lineCanCreate && line.chargeId != null ? (
                                <Button
                                  variant="primary"
                                  size="sm"
                                  type="button"
                                  loading={createMutation.isPending}
                                  onClick={() => void handleCreate(line.chargeId)}
                                >
                                  <CreditCard className="h-3.5 w-3.5" aria-hidden="true" />
                                  <span>
                                    {linePending
                                      ? 'Tiếp tục thanh toán'
                                      : lineExpired
                                        ? 'Tạo mã QR mới'
                                        : unpaidSelectableCount > 1
                                          ? 'Thanh toán riêng'
                                          : 'Tạo mã VietQR'}
                                  </span>
                                </Button>
                              ) : null}
                            </div>
                          </div>

                          {confirmingDeclineOrderId === line.diagnosticOrderId &&
                          line.diagnosticOrderId != null ? (
                            <div className="mt-2 rounded-xl border border-rose-200 bg-rose-50/60 p-3 space-y-2">
                              <p className="font-semibold text-navy">
                                Xác nhận không thực hiện dịch vụ tùy chọn "{line.serviceName}"?
                              </p>
                              <input
                                type="text"
                                aria-label="Lý do không thực hiện dịch vụ tùy chọn"
                                value={declineReason}
                                onChange={(e) => setDeclineReason(e.target.value)}
                                placeholder="Lý do (tùy chọn)..."
                                className="w-full rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-ink focus:border-primary focus:outline-none"
                              />
                              <div className="flex items-center justify-end gap-2">
                                <Button
                                  variant="secondary"
                                  size="sm"
                                  type="button"
                                  onClick={() => setConfirmingDeclineOrderId(null)}
                                >
                                  Giữ lại
                                </Button>
                                <Button
                                  variant="danger"
                                  size="sm"
                                  type="button"
                                  loading={declineMutation.isPending}
                                  onClick={() => void handleConfirmDecline(line.diagnosticOrderId!)}
                                >
                                  Xác nhận không thực hiện
                                </Button>
                              </div>
                            </div>
                          ) : null}
                        </div>
                      );
                    })}
                  </div>
                </Card>
              ) : isExpired ? (
                <PaymentNoticeCard
                  tone="warning"
                  title="Mã QR đã hết hạn"
                  message="Mã thanh toán trước đó không còn hiệu lực. Tạo mã mới để tiếp tục thanh toán."
                >
                  <Button
                    variant="primary"
                    size="lg"
                    type="button"
                    loading={createMutation.isPending}
                    disabled={!canCreate}
                    onClick={() => void handleCreate(singleChargeId)}
                  >
                    Tạo mã QR mới
                  </Button>
                </PaymentNoticeCard>
              ) : canCreate ? (
                <Card className="p-6">
                  <p className="text-[11px] font-semibold uppercase tracking-wide text-sub">
                    Số tiền cần thanh toán
                  </p>
                  <p className="mt-1 text-3xl font-extrabold text-ink">
                    {formatVnd(charges.amountDue)}
                  </p>
                  <p className="mt-2 text-sm text-sub">
                    Thanh toán cho lượt khám{' '}
                    <span className="font-mono font-semibold text-ink">
                      #{charges.visitCode ?? visitIdOrCode}
                    </span>
                    . Mã QR có hiệu lực 15 phút và tự cập nhật khi nhận được tiền.
                  </p>
                  <div className="mt-5">
                    <Button
                      variant="primary"
                      size="lg"
                      type="button"
                      loading={createMutation.isPending}
                      onClick={() => void handleCreate(singleChargeId)}
                    >
                      <CreditCard className="h-4 w-4" aria-hidden="true" />
                      <span>{createLabel}</span>
                    </Button>
                  </div>
                </Card>
              ) : (
                <PaymentNoticeCard title="Chưa thể tạo mã QR" message={blockedMessage(charges)} />
              )}
            </div>
          ) : null}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}

function blockedMessage(charges: {
  hasUnpricedPayableCharges: boolean;
  amountDue: number | null;
  paymentMethod: string | null;
  visitStatus: string | null;
}): string {
  if (charges.hasUnpricedPayableCharges || charges.amountDue === null) {
    return 'Chưa xác định được số tiền cần thanh toán cho lượt khám này. Vui lòng liên hệ quầy thu ngân.';
  }
  if (charges.amountDue === 0) {
    return 'Lượt khám không có khoản phải thu.';
  }
  if (charges.paymentMethod?.toUpperCase() === 'CASHIER') {
    return 'Lượt khám được thu tại quầy. Vui lòng thanh toán trực tiếp tại quầy thu ngân.';
  }
  if (['CANCELLED', 'NO_SHOW'].includes(charges.visitStatus?.toUpperCase() ?? '')) {
    return 'Lượt khám không thể thanh toán vì đã hủy hoặc không đến.';
  }
  return 'Hiện chưa thể tạo mã QR cho lượt khám này. Vui lòng liên hệ quầy thu ngân.';
}
