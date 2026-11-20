import { ArrowLeft, RefreshCw } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { PaymentSummaryCard } from '../components/payment-summary-card';
import { QrPaymentPanel } from '../components/qr-payment-panel';
import { useGenerateQrPaymentMutation, useVisitChargesQuery } from '../hooks/use-payment';
import { chargeStatusLabel, formatDateTime, formatVnd } from '../lib/payment-format';

export function PatientPaymentDetailPage() {
  const { visitCode = '' } = useParams<{ visitCode: string }>();
  const chargesQuery = useVisitChargesQuery(visitCode);
  const createQr = useGenerateQrPaymentMutation(visitCode);

  const charges = chargesQuery.data;
  const created = createQr.data;
  const isPaid = charges?.paymentStatus === 'PAID';
  const showPanel =
    Boolean(created) || (charges?.paymentStatus === 'PENDING' && Boolean(charges?.paymentCode));

  const panelCode = created?.paymentCode ?? charges?.paymentCode ?? '';
  const panelAmount = created?.amount ?? charges?.paymentAmount ?? charges?.amountDue ?? null;

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

              <Card>
                <CardHeader className="flex flex-wrap items-center justify-between gap-2">
                  <h2 className="text-sm font-bold text-ink">Khoản phí chi tiết</h2>
                  <Badge tone="slate">{charges.charges.length} khoản</Badge>
                </CardHeader>
                <CardContent className="p-0">
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
                          Ghi chú
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {charges.charges.map((line, index) => (
                        <tr
                          key={line.chargeId ?? `line-${index}`}
                          className="border-t border-border"
                        >
                          <td className="px-6 py-3">
                            <span className="font-semibold text-ink">
                              {line.serviceName ?? 'Chưa rõ dịch vụ'}
                            </span>
                            {line.serviceCode ? (
                              <span className="ml-2 font-mono text-[11px] text-sub">
                                {line.serviceCode}
                              </span>
                            ) : null}
                          </td>
                          <td className="px-4 py-3 font-mono text-[11px] text-sub">
                            {line.diagnosticOrderId ? `#${line.diagnosticOrderId}` : '—'}
                          </td>
                          <td className="px-4 py-3 text-right font-semibold text-ink">
                            {formatVnd(line.unitPrice)}
                          </td>
                          <td className="px-6 py-3">
                            <div className="flex flex-wrap gap-1.5">
                              {line.payable === false ? (
                                <Badge tone="emerald">Miễn thu</Badge>
                              ) : null}
                              {line.chargeDataStatus !== 'VALID' ? (
                                <Badge tone="rose">
                                  {chargeStatusLabel(line.chargeDataStatus)}
                                </Badge>
                              ) : null}
                              {line.paymentRequirement ? (
                                <Badge tone="slate">{line.paymentRequirement}</Badge>
                              ) : null}
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
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

              {isPaid ? (
                <Alert tone="success">
                  Lượt khám đã được thanh toán đủ. Không cần thanh toán thêm.
                </Alert>
              ) : null}

              {!showPanel && charges.canPayByQr ? (
                <div className="space-y-3">
                  <Button
                    onClick={() => createQr.mutate()}
                    loading={createQr.isPending}
                    disabled={!visitCode.trim()}
                  >
                    Tạo mã VietQR
                  </Button>
                  {createQr.isError ? (
                    <Alert tone="error">{getErrorMessage(createQr.error)}</Alert>
                  ) : null}
                </div>
              ) : null}

              {showPanel && panelCode ? (
                <QrPaymentPanel
                  paymentCode={panelCode}
                  amount={panelAmount}
                  status={created?.status ?? charges.paymentStatus}
                  qrPayload={created?.qrPayload ?? null}
                  bankBin={created?.bankBin ?? null}
                  accountNumber={created?.accountNumber ?? null}
                  accountName={created?.accountName ?? null}
                  transferContent={created?.transferContent ?? panelCode}
                  expiresAtLabel={null}
                />
              ) : null}
            </>
          ) : null}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
