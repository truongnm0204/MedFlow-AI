import { ArrowLeft, ChevronRight, CreditCard, Home, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Card } from '@/shared/ui/card';

import { PaymentNoticeCard } from '../components/payment-notice-card';
import { PaymentPageSkeleton } from '../components/payment-page-skeleton';
import { PaymentPaidCard } from '../components/payment-paid-card';
import { PaymentQrCard } from '../components/payment-qr-card';
import { useCreateQrPaymentMutation } from '../hooks/use-create-qr-payment';
import { useRemainingMs } from '../hooks/use-remaining-ms';
import { useVisitChargesQuery } from '../hooks/use-visit-charges';
import type { QrPaymentResponseDto } from '../types/payment.types';
import { PAYMENT_QR_TTL_MS, formatVnd } from '../utils/payment-format';

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
  const [qr, setQr] = useState<QrPaymentResponseDto | null>(null);
  const [expiresAt, setExpiresAt] = useState<number | null>(null);
  const remainingMs = useRemainingMs(expiresAt);

  const paymentStatus = charges?.paymentStatus?.toUpperCase() ?? '';
  const isPaid = charges?.settlementStatus === 'PAID' || paymentStatus === 'PAID';
  const isClosed = paymentStatus === 'CANCELLED' || paymentStatus === 'FAILED';
  const isExpired = paymentStatus === 'EXPIRED' || (expiresAt !== null && remainingMs === 0);
  const isPayable = Boolean(charges?.canPayByQr);
  const canCreate =
    Boolean(charges) &&
    !isPaid &&
    !isClosed &&
    (isPayable || isExpired || paymentStatus === 'PENDING');
  const showQr = qr !== null && !isPaid && !isClosed && !isExpired;

  const handleCreate = async () => {
    try {
      const created = await createMutation.mutateAsync();
      setQr(created);
      setExpiresAt(Date.now() + PAYMENT_QR_TTL_MS);
    } catch {
      /* Lỗi được hiển thị qua createMutation.error. */
    }
  };

  const createLabel = isExpired
    ? 'Tạo mã QR mới'
    : paymentStatus === 'PENDING'
      ? 'Tiếp tục thanh toán'
      : 'Tạo mã QR thanh toán';

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
              {createMutation.isError ? (
                <Alert tone="error">{getErrorMessage(createMutation.error)}</Alert>
              ) : null}

              {isPaid ? (
                <PaymentPaidCard
                  amount={charges.paymentAmount ?? charges.payableTotal}
                  paidAt={charges.paidAt}
                  reference={charges.externalReference}
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
                </>
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
                    onClick={() => void handleCreate()}
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
                      onClick={() => void handleCreate()}
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
