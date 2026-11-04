import { Link, useParams } from 'react-router-dom';
import { ArrowRight, ChevronRight, Home, Info, QrCode, RefreshCw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { QueueCallBoard } from '../components/queue-call-board';
import { QueueHeroCard } from '../components/queue-hero-card';
import { QueueVisitTimeline } from '../components/queue-visit-timeline';
import { useAppointmentQueueStatusQuery } from '../hooks/use-appointment-queue';

export function AppointmentQueueTrackingPage() {
  const { appointmentId = '' } = useParams<{ appointmentId: string }>();
  const { data, isLoading, isError, error, isFetching, refetch } =
    useAppointmentQueueStatusQuery(appointmentId);

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Theo Dõi Hàng Đợi Khám Trực Tiếp</span>
          </nav>

          <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
            <h1 className="text-2xl font-extrabold text-ink sm:text-3xl">
              Theo Dõi Hàng Đợi Khám Trực Tiếp
            </h1>

            {isError && data ? (
              <span className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-3.5 py-1.5 text-xs font-bold text-amber-800">
                <span className="h-2 w-2 rounded-full bg-amber-500" aria-hidden="true" />
                <span>MẤT KẾT NỐI – ĐANG THỬ LẠI</span>
              </span>
            ) : (
              <span className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-3.5 py-1.5 text-xs font-bold text-emerald-700">
                <span className="h-2 w-2 rounded-full bg-emerald-500" aria-hidden="true" />
                <span>ĐANG KẾT NỐI TRỰC TIẾP (Cập nhật tự động 10s)</span>
              </span>
            )}
          </div>

          {isLoading && !data ? (
            <div className="flex items-center justify-center rounded-2xl border border-border bg-white p-12 shadow-xs">
              <Spinner className="h-6 w-6" />
            </div>
          ) : isError && !data ? (
            <div className="space-y-4">
              <Alert tone="error">{getErrorMessage(error)}</Alert>
              <Link
                to="/appointments"
                className="inline-flex items-center justify-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark"
              >
                <span>Về danh sách Lịch hẹn của tôi</span>
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
            </div>
          ) : data ? (
            <div className="space-y-5">
              <QueueHeroCard data={data} />

              <QueueCallBoard data={data} />

              <QueueVisitTimeline data={data} />

              <section
                aria-label="Hướng dẫn dành cho bệnh nhân"
                className="flex items-start gap-3 rounded-2xl border border-sky-200 bg-sky-50 p-5 text-xs leading-relaxed text-ink"
              >
                <Info className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
                <div>
                  <h2 className="font-bold text-ink">Hướng dẫn dành cho Quý bệnh nhân:</h2>
                  <p className="mt-1 text-sub">{data.guidanceNote}</p>
                </div>
              </section>

              <div className="flex flex-col gap-3 sm:flex-row sm:justify-between">
                <Link
                  to={`/appointments/${data.appointmentId}/print`}
                  className="inline-flex items-center justify-center gap-2 rounded-xl border border-border bg-white px-6 py-3 text-sm font-semibold text-navy transition-colors hover:bg-canvas"
                >
                  <QrCode className="h-4 w-4" aria-hidden="true" />
                  <span>Xem phiếu hẹn &amp; Mã QR</span>
                </Link>

                <Button variant="primary" size="lg" type="button" onClick={() => void refetch()}>
                  <RefreshCw
                    className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`}
                    aria-hidden="true"
                  />
                  <span>Làm Mới Trực Tiếp (Refresh)</span>
                </Button>
              </div>
            </div>
          ) : null}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
