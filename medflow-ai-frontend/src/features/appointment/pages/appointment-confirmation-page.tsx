import { Link, useParams } from 'react-router-dom';
import { ArrowRight, ChevronRight, Eye, Home, Printer } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Spinner } from '@/shared/ui/spinner';

import { BookingStepper } from '../components/booking-stepper';
import { ConfirmationArrivalGuide } from '../components/confirmation-arrival-guide';
import { ConfirmationQueueCard } from '../components/confirmation-queue-card';
import { ConfirmationSlipDetails } from '../components/confirmation-slip-details';
import { ConfirmationSuccessHero } from '../components/confirmation-success-hero';
import { CANCELLED_STATUSES } from '../data/appointment-list-mock';
import { useAppointmentDetailQuery } from '../hooks/use-appointment-detail';

const SECONDARY_LINK_CLASSES =
  'inline-flex items-center justify-center font-semibold transition-colors bg-white text-navy border border-border hover:bg-canvas px-6 py-3 text-sm rounded-xl gap-2';
const PRIMARY_LINK_CLASSES =
  'inline-flex items-center justify-center font-semibold transition-colors bg-primary text-white hover:bg-primary-dark shadow-sm px-6 py-3 text-sm rounded-xl gap-2';

export function AppointmentConfirmationPage() {
  const { appointmentId = '' } = useParams<{ appointmentId: string }>();
  const { data: appointment, isLoading, isError, error } = useAppointmentDetailQuery(appointmentId);

  const isCancelled = appointment ? CANCELLED_STATUSES.includes(appointment.status) : false;

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
            <Link to="/appointment/doctors" className="transition-colors hover:text-primary">
              Đặt khám
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Xác nhận Đặt hẹn Thành công</span>
          </nav>

          {isLoading ? (
            <div className="flex items-center justify-center rounded-2xl border border-border bg-white p-12 shadow-xs">
              <Spinner className="h-6 w-6" />
            </div>
          ) : isError || !appointment ? (
            <div className="space-y-4">
              <Alert tone="error">{getErrorMessage(error)}</Alert>
              <Link to="/appointments" className={PRIMARY_LINK_CLASSES}>
                <span>Về danh sách Lịch hẹn của tôi</span>
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
            </div>
          ) : (
            <div className="space-y-5">
              <BookingStepper mode={appointment.doctorId ? 'doctor' : 'service'} currentStep={4} />

              {isCancelled && (
                <Alert tone="info" className="border-amber-200 bg-amber-50 text-amber-800">
                  Lịch hẹn này đã bị hủy.
                </Alert>
              )}

              <ConfirmationSuccessHero appointmentCode={appointment.appointmentCode} />

              {!isCancelled && <ConfirmationQueueCard appointment={appointment} />}

              <ConfirmationSlipDetails appointment={appointment} />

              <ConfirmationArrivalGuide appointment={appointment} />

              <div className="grid gap-3 sm:grid-cols-3">
                <Link
                  to={`/appointments/${appointment.id}/print`}
                  className={SECONDARY_LINK_CLASSES}
                >
                  <Printer className="h-4 w-4" aria-hidden="true" />
                  <span>Tải phiếu hẹn (PDF) / In phiếu</span>
                </Link>

                <Link to={`/appointments/${appointment.id}`} className={SECONDARY_LINK_CLASSES}>
                  <Eye className="h-4 w-4" aria-hidden="true" />
                  <span>Xem chi tiết lịch hẹn</span>
                </Link>

                <Link to="/appointments" className={PRIMARY_LINK_CLASSES}>
                  <span>Về danh sách Lịch hẹn của tôi</span>
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
              </div>
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
