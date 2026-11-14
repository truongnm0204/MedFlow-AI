import { CalendarDays, ChevronRight, Receipt, RefreshCw, Stethoscope } from 'lucide-react';
import { Link } from 'react-router-dom';

import { useAppointmentListQuery } from '@/features/appointment/hooks/use-appointment-list';
import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

export function PatientPaymentListPage() {
  const { data, isPending, isError, error, refetch, isFetching } = useAppointmentListQuery({
    tab: 'ALL',
    pageSize: 50,
  });

  const visits = (data?.items ?? []).filter((item) => Boolean(item.visitCode));

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 py-8">
        <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
          <div className="mb-6">
            <h1 className="text-xl font-bold text-ink">Viện phí của tôi</h1>
            <p className="mt-1 text-xs text-sub">
              Chọn một lượt khám đã tiếp nhận để xem khoản phí và thanh toán bằng VietQR.
            </p>
          </div>

          {isPending ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải danh sách lượt khám...
            </div>
          ) : null}

          {isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void refetch()}
                loading={isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {!isPending && !isError && visits.length === 0 ? (
            <Alert tone="info">
              Chưa có lượt khám nào đã tiếp nhận. Viện phí chỉ hiển thị sau khi bạn được tiếp đón
              tại cơ sở khám.
            </Alert>
          ) : null}

          <div className="space-y-3">
            {visits.map((item) => (
              <Card key={item.id}>
                <CardContent className="flex flex-wrap items-center justify-between gap-4">
                  <div className="min-w-0 space-y-1.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs font-bold text-ink">
                        {item.appointmentCode}
                      </span>
                      <Badge tone="slate">{item.paymentStatusLabel}</Badge>
                    </div>
                    <p className="flex items-center gap-1.5 text-xs text-sub">
                      <Stethoscope className="h-3.5 w-3.5" aria-hidden="true" />
                      {item.doctorName}
                      <span aria-hidden="true">•</span>
                      {item.serviceName}
                    </p>
                    <p className="flex items-center gap-1.5 text-xs text-sub">
                      <CalendarDays className="h-3.5 w-3.5" aria-hidden="true" />
                      {item.scheduledDateFullLabel} ({item.scheduledTimeRange})
                    </p>
                  </div>

                  <Link
                    to={`/payments/${encodeURIComponent(item.visitCode ?? '')}`}
                    className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-dark"
                  >
                    <Receipt className="h-3.5 w-3.5" aria-hidden="true" />
                    Xem viện phí
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </Link>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
