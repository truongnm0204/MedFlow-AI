import { ArrowLeft, QrCode, RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { generateQrPaymentApi } from '../api/payment.api';
import { PaymentSummaryCard } from '../components/payment-summary-card';
import { QrPaymentPanel } from '../components/qr-payment-panel';
import { useGenerateQrPaymentMutation, useVisitChargesQuery } from '../hooks/use-payment';
import type {
  GenerateQrPaymentResponse,
  LineSettlementStatus,
  VisitChargeLine,
  VisitChargesResponse,
} from '../types/payment.types';
import {
  chargeStatusLabel,
  formatDateTime,
  formatVnd,
  lineSettlementLabel,
} from '../utils/payment-format';

function resolveLineSettlement(
  line: VisitChargeLine,
  charges: VisitChargesResponse,
): LineSettlementStatus {
  if (line.lineSettlementStatus) {
    return line.lineSettlementStatus;
  }
  if (line.chargeStatus === 'CANCELLED_REFUND_PENDING') {
    return 'MISMATCH';
  }
  if (line.chargeStatus === 'CANCELLED') {
    return 'CANCELLED';
  }
  if (line.chargeDataStatus !== 'VALID') {
    return 'UNDETERMINED';
  }
  if (line.payable === false || line.unitPrice === 0) {
    return 'WAIVED';
  }
  if (charges.charges.length === 1) {
    return charges.settlementStatus;
  }
  return 'UNPAID';
}

function resolveLineCanPayByQr(
  line: VisitChargeLine,
  lineSettlement: LineSettlementStatus,
  charges: VisitChargesResponse,
): boolean {
  if (line.canPayByQr !== undefined) {
    return line.canPayByQr;
  }
  return (
    line.payable === true &&
    line.chargeDataStatus === 'VALID' &&
    (line.unitPrice ?? 0) > 0 &&
    lineSettlement === 'UNPAID' &&
    charges.canPayByQr
  );
}

const LINE_SETTLEMENT_TONES: Record<
  LineSettlementStatus,
  'emerald' | 'amber' | 'rose' | 'slate' | 'sky'
> = {
  PAID: 'emerald',
  WAIVED: 'emerald',
  UNPAID: 'amber',
  MISMATCH: 'rose',
  CANCELLED: 'slate',
  PAID_NOT_PERFORMED: 'slate',
  UNDETERMINED: 'slate',
};

export function PatientPaymentDetailPage() {
  const { visitCode = '' } = useParams<{ visitCode: string }>();
  const chargesQuery = useVisitChargesQuery(visitCode);
  const createQr = useGenerateQrPaymentMutation(visitCode);

  const [hydratedQr, setHydratedQr] = useState<GenerateQrPaymentResponse | null>(null);
  const hydratedKeyRef = useRef<string>('');

  const charges = chargesQuery.data;
  const activeQr = createQr.data ?? hydratedQr;

  // BUG-P3-02: Only mark the visit fully settled when settlementStatus === 'PAID' (all active required charges paid)
  const isAllSettled = charges?.settlementStatus === 'PAID';
  const isMismatch = charges?.settlementStatus === 'MISMATCH';

  const pendingLine = charges?.charges.find(
    (l) => l.paymentStatus?.toUpperCase() === 'PENDING' && Boolean(l.paymentCode),
  );
  const pendingTargetCode =
    pendingLine?.paymentCode ??
    (charges?.paymentStatus?.toUpperCase() === 'PENDING' ? charges.paymentCode : null);
  const pendingTargetChargeId = pendingLine?.chargeId ?? null;

  // BUG-P3-07: Automatically hydrate active PENDING QR payload on page reload
  useEffect(() => {
    const trimmed = visitCode.trim();
    if (!trimmed || !pendingTargetCode || activeQr) {
      return;
    }

    const hydrationKey = `${trimmed}:${pendingTargetChargeId ?? 'visit'}:${pendingTargetCode}`;
    if (hydratedKeyRef.current === hydrationKey) {
      return;
    }
    hydratedKeyRef.current = hydrationKey;

    let cancelled = false;
    void generateQrPaymentApi(trimmed, pendingTargetChargeId)
      .then((res) => {
        if (!cancelled) {
          setHydratedQr(res);
        }
      })
      .catch(() => {
        /* Passive hydration fallback keeps transfer code visible without throwing an error banner. */
      });

    return () => {
      cancelled = true;
    };
  }, [activeQr, pendingTargetChargeId, pendingTargetCode, visitCode]);

  const activeQrChargeLine =
    activeQr?.visitChargeId != null
      ? charges?.charges.find((l) => l.chargeId === activeQr.visitChargeId)
      : undefined;
  const activeQrAlreadySettled =
    isAllSettled ||
    (activeQrChargeLine != null && resolveLineSettlement(activeQrChargeLine, charges!) === 'PAID');

  const showPanel = !activeQrAlreadySettled && (Boolean(activeQr) || Boolean(pendingTargetCode));

  const panelCode = activeQr?.paymentCode ?? pendingTargetCode ?? '';
  const panelAmount =
    activeQr?.amount ??
    pendingLine?.unitPrice ??
    charges?.paymentAmount ??
    charges?.amountDue ??
    null;
  const panelStatus =
    activeQr?.status ?? pendingLine?.paymentStatus ?? charges?.paymentStatus ?? null;

  const hasPerChargeRows = Boolean(charges && charges.charges.length > 0);
  const payableRowButtons =
    charges?.charges.filter((line) => {
      const settlement = resolveLineSettlement(line, charges);
      return resolveLineCanPayByQr(line, settlement, charges);
    }) ?? [];

  const handleGenerateQr = (visitChargeId?: number | null) => {
    setHydratedQr(null);
    const freshKey =
      typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? `qr-${crypto.randomUUID().slice(0, 36)}`
        : `qr-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
    createQr.mutate(
      visitChargeId != null
        ? { visitChargeId, idempotencyKey: freshKey }
        : { idempotencyKey: freshKey },
    );
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 py-8">
        <div className="mx-auto max-w-4xl space-y-4 px-4 sm:px-6 lg:px-8">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0">
              <Link
                to="/payments"
                className="inline-flex items-center gap-1.5 text-xs font-semibold text-sub hover:text-primary"
              >
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                Danh sách viện phí
              </Link>
              <h1 className="mt-2 text-xl font-bold text-ink">
                Viện phí lượt khám{' '}
                <span className="font-mono text-base">{charges?.visitCode ?? visitCode}</span>
              </h1>
              {charges?.visitStatus ? (
                <p className="mt-1 text-xs text-sub">Trạng thái lượt khám: {charges.visitStatus}</p>
              ) : null}
            </div>

            {chargesQuery.isFetching ? <Spinner className="h-4 w-4" /> : null}
          </div>

          {!visitCode.trim() ? <Alert tone="error">Mã lượt khám không hợp lệ.</Alert> : null}

          {chargesQuery.isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(chargesQuery.error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void chargesQuery.refetch()}
                loading={chargesQuery.isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {chargesQuery.isPending && visitCode.trim() ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải viện phí...
            </div>
          ) : null}

          {charges ? (
            <>
              <PaymentSummaryCard
                settlementStatus={charges.settlementStatus}
                payableTotal={charges.payableTotal}
                recognizedPaidTotal={charges.recognizedPaidTotal}
                amountDue={charges.amountDue}
              />

              {createQr.isError ? (
                <Alert tone="error">{getErrorMessage(createQr.error)}</Alert>
              ) : null}

              {isMismatch ? (
                <Alert tone="warning">
                  Có khoản thanh toán lệch số tiền hoặc phát sinh trên dịch vụ đã hủy. Vui lòng liên
                  hệ quầy thu ngân để đối soát / hoàn tiền.
                </Alert>
              ) : null}

              <Card>
                <CardHeader className="flex flex-wrap items-center justify-between gap-2">
                  <h2 className="text-sm font-bold text-ink">Khoản phí chi tiết</h2>
                  <Badge tone="slate">{charges.charges.length} khoản</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <div className="overflow-x-auto">
                    <table className="w-full text-left text-xs">
                      <thead className="bg-canvas text-[11px] uppercase tracking-wide text-sub">
                        <tr>
                          <th scope="col" className="px-6 py-3 font-semibold">
                            Dịch vụ
                          </th>
                          <th scope="col" className="px-4 py-3 font-semibold">
                            Mã chỉ định
                          </th>
                          <th scope="col" className="px-4 py-3 text-right font-semibold">
                            Số tiền
                          </th>
                          <th scope="col" className="px-6 py-3 font-semibold">
                            Trạng thái &amp; Thanh toán
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {charges.charges.map((line, index) => {
                          const lineSettlement = resolveLineSettlement(line, charges);
                          const canPayLine = resolveLineCanPayByQr(line, lineSettlement, charges);
                          const isLinePending = line.paymentStatus?.toUpperCase() === 'PENDING';
                          const chargeTypeLabel =
                            line.chargeType === 'DIAGNOSTIC' || line.diagnosticOrderId != null
                              ? 'Cận lâm sàng'
                              : 'Phí khám';

                          return (
                            <tr
                              key={line.chargeId ?? `line-${index}`}
                              className="border-t border-border"
                            >
                              <td className="px-6 py-3">
                                <div className="flex flex-wrap items-center gap-1.5">
                                  <span className="font-semibold text-ink">
                                    {line.serviceName ?? 'Chưa rõ dịch vụ'}
                                  </span>
                                  {line.serviceCode ? (
                                    <span className="font-mono text-[11px] text-sub">
                                      {line.serviceCode}
                                    </span>
                                  ) : null}
                                  <Badge tone="sky">{chargeTypeLabel}</Badge>
                                </div>
                              </td>
                              <td className="px-4 py-3 font-mono text-[11px] text-sub">
                                {line.diagnosticOrderId ? `#${line.diagnosticOrderId}` : '—'}
                              </td>
                              <td className="px-4 py-3 text-right font-semibold text-ink">
                                {formatVnd(line.unitPrice)}
                              </td>
                              <td className="px-6 py-3">
                                <div className="flex flex-wrap items-center justify-between gap-2">
                                  <div className="flex flex-wrap items-center gap-1.5">
                                    {line.payable === false ? (
                                      <Badge tone="emerald">Miễn thu</Badge>
                                    ) : (
                                      <Badge tone={LINE_SETTLEMENT_TONES[lineSettlement]}>
                                        {lineSettlementLabel(lineSettlement)}
                                      </Badge>
                                    )}
                                    {line.chargeDataStatus !== 'VALID' ? (
                                      <Badge tone="rose">
                                        {chargeStatusLabel(line.chargeDataStatus)}
                                      </Badge>
                                    ) : null}
                                    {line.paymentCode ? (
                                      <span className="font-mono text-[11px] font-semibold text-sub">
                                        {line.paymentCode}
                                      </span>
                                    ) : null}
                                    {line.paymentRequirement ? (
                                      <Badge tone="slate">{line.paymentRequirement}</Badge>
                                    ) : null}
                                  </div>

                                  {canPayLine && payableRowButtons.length > 1 ? (
                                    <Button
                                      size="sm"
                                      onClick={() => handleGenerateQr(line.chargeId)}
                                      loading={createQr.isPending}
                                      disabled={!visitCode.trim()}
                                    >
                                      <QrCode className="h-3.5 w-3.5" aria-hidden="true" />
                                      <span>Tạo mã VietQR</span>
                                    </Button>
                                  ) : isLinePending && line.chargeId != null ? (
                                    <Button
                                      variant="secondary"
                                      size="sm"
                                      onClick={() => handleGenerateQr(line.chargeId)}
                                      loading={createQr.isPending}
                                      disabled={!visitCode.trim()}
                                    >
                                      <span>Xem mã VietQR</span>
                                    </Button>
                                  ) : null}
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </CardContent>
              </Card>

              {charges.paymentId !== null ? (
                <Card>
                  <CardHeader>
                    <h2 className="text-sm font-bold text-ink">Giao dịch gần nhất</h2>
                  </CardHeader>
                  <CardContent className="space-y-1.5 text-xs text-sub">
                    <p>
                      Mã thanh toán:{' '}
                      <span className="font-mono font-semibold text-ink">
                        {charges.paymentCode ?? '—'}
                      </span>
                    </p>
                    <p>Số tiền: {formatVnd(charges.paymentAmount)}</p>
                    {charges.paidAt ? (
                      <p>Đã ghi nhận lúc: {formatDateTime(charges.paidAt)}</p>
                    ) : null}
                    {charges.externalReference ? (
                      <p>
                        Mã giao dịch ngân hàng:{' '}
                        <span className="font-mono">{charges.externalReference}</span>
                      </p>
                    ) : null}
                  </CardContent>
                </Card>
              ) : null}

              {isAllSettled ? (
                <Alert tone="success">
                  Lượt khám đã được thanh toán đủ. Không cần thanh toán thêm.
                </Alert>
              ) : null}

              {!showPanel &&
              !isAllSettled &&
              ((!hasPerChargeRows && charges.canPayByQr) || payableRowButtons.length === 1) ? (
                <div className="space-y-3">
                  <Button
                    onClick={() =>
                      handleGenerateQr(
                        payableRowButtons.length === 1 ? payableRowButtons[0]?.chargeId : undefined,
                      )
                    }
                    loading={createQr.isPending}
                    disabled={!visitCode.trim()}
                  >
                    Tạo mã VietQR
                  </Button>
                </div>
              ) : null}

              {showPanel && panelCode ? (
                <div className="space-y-3">
                  <QrPaymentPanel
                    paymentCode={panelCode}
                    amount={panelAmount}
                    status={panelStatus}
                    qrPayload={activeQr?.qrPayload ?? null}
                    bankBin={activeQr?.bankBin ?? null}
                    accountNumber={activeQr?.accountNumber ?? null}
                    accountName={activeQr?.accountName ?? null}
                    transferContent={activeQr?.transferContent ?? panelCode}
                    expiresAtLabel={null}
                  />
                  {!isAllSettled &&
                  (panelStatus === 'EXPIRED' ||
                    panelStatus === 'CANCELLED' ||
                    panelStatus === 'MISMATCH') ? (
                    <Button
                      onClick={() =>
                        handleGenerateQr(
                          activeQr?.visitChargeId ??
                            (payableRowButtons.length === 1
                              ? payableRowButtons[0]?.chargeId
                              : undefined),
                        )
                      }
                      loading={createQr.isPending}
                      disabled={!visitCode.trim()}
                    >
                      Tạo mã VietQR mới
                    </Button>
                  ) : null}
                </div>
              ) : null}
            </>
          ) : null}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
