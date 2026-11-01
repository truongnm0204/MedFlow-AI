import { useMemo } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  ChevronRight,
  ClipboardCheck,
  Home,
} from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { BookingStepper } from '../components/booking-stepper';
import { ReviewCostSection } from '../components/review-cost-section';
import { ReviewPatientSection } from '../components/review-patient-section';
import { ReviewPolicySection } from '../components/review-policy-section';
import { ReviewScheduleSection } from '../components/review-schedule-section';
import { useAvailableSlots, useServiceAvailableShifts } from '../hooks/use-available-slots';
import { useCreateAppointmentMutation } from '../hooks/use-create-appointment';
import { useBookingStore } from '../store/booking.store';
import {
  parseBookingReviewRequest,
  resolveDoctorBookingSummary,
  resolveServiceBookingSummary,
} from '../utils/booking-review-summary';

function ReviewInvalidState({ mode }: { mode: string | null }) {
  const backPath = mode === 'service' ? '/appointment/services' : '/appointment/doctors';

  return (
    <div className="rounded-2xl border border-border bg-white p-8 text-center shadow-xs">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-rose-50 text-rose-600">
        <AlertCircle className="h-6 w-6" aria-hidden="true" />
      </div>
      <h1 className="mt-4 text-lg font-bold text-ink">
        Thông tin đặt khám không đầy đủ hoặc khung giờ đã hết chỗ
      </h1>
      <p className="mt-1 text-xs text-sub">
        Vui lòng quay lại danh mục để chọn bác sĩ, dịch vụ và khung giờ khám còn trống.
      </p>
      <Link
        to={backPath}
        className="mt-5 inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark"
      >
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        <span>Quay lại chọn bác sĩ / dịch vụ</span>
      </Link>
    </div>
  );
}

export function AppointmentReviewPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const symptomNote = useBookingStore((s) => s.symptomNote);
  const resetBooking = useBookingStore((s) => s.reset);

  const request = useMemo(
    () => parseBookingReviewRequest(searchParams, symptomNote),
    [searchParams, symptomNote],
  );

  const { data: doctorData, isLoading: isDoctorLoading } = useAvailableSlots(
    {
      doctorId: request?.doctorId,
      serviceId: request?.serviceId,
      dateIso: request?.dateIso,
    },
    { enabled: request?.mode === 'doctor' },
  );

  const { data: serviceData, isLoading: isServiceLoading } = useServiceAvailableShifts(
    {
      serviceId: request?.serviceId,
      dateIso: request?.dateIso,
    },
    { enabled: request?.mode === 'service' },
  );

  const isLoading =
    request?.mode === 'doctor'
      ? isDoctorLoading
      : request?.mode === 'service'
        ? isServiceLoading
        : false;

  const summary = useMemo(() => {
    if (!request) return null;
    if (request.mode === 'doctor') {
      return doctorData ? resolveDoctorBookingSummary(request, doctorData) : null;
    }
    return serviceData ? resolveServiceBookingSummary(request, serviceData) : null;
  }, [request, doctorData, serviceData]);

  const mutation = useCreateAppointmentMutation();

  const handleBackToSlotSelection = () => {
    if (!request) return;
    if (request.mode === 'doctor') {
      navigate(`/appointment/doctors/${request.doctorId}/booking`);
    } else {
      navigate(`/appointment/services/${request.serviceId}/booking`);
    }
  };

  const handleConfirmBooking = () => {
    if (!request) return;
    mutation.mutate(request, {
      onSuccess: (apt) => {
        resetBooking();
        navigate(`/appointment/confirmation/${apt.id}`, { replace: true });
      },
    });
  };

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
            <Link
              to={request?.mode === 'service' ? '/appointment/services' : '/appointment/doctors'}
              className="transition-colors hover:text-primary"
            >
              Đặt khám
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Soát xét Thông tin Đặt hẹn</span>
          </nav>

          {!request || (!isLoading && !summary) ? (
            <ReviewInvalidState mode={searchParams.get('mode')} />
          ) : isLoading || !summary ? (
            <div className="flex items-center justify-center rounded-2xl border border-border bg-white p-12 shadow-xs">
              <Spinner className="h-6 w-6" />
            </div>
          ) : (
            <div className="space-y-5">
              <section className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6">
                <span className="inline-flex items-center gap-1.5 rounded-lg border border-sky-200 bg-sky-50 px-2.5 py-1 text-[11px] font-bold text-primary uppercase">
                  <ClipboardCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>BƯỚC CHUẨN BỊ CUỐI CÙNG</span>
                </span>
                <h1 className="mt-2.5 text-2xl font-extrabold text-ink sm:text-3xl">
                  Soát xét Thông tin Đặt khám Ngoại trú
                </h1>
                <p className="mt-1.5 text-xs leading-relaxed text-sub sm:text-sm">
                  Vui lòng kiểm tra kỹ lưỡng các thông tin bên dưới trước khi xác nhận tạo lịch hẹn
                  và nhận số thứ tự giữ trước tại buồng khám.
                </p>
              </section>

              <BookingStepper mode={request.mode} currentStep={3} />

              <ReviewPatientSection
                patient={summary.patient}
                reasonForVisit={request.reasonForVisit}
              />

              <ReviewScheduleSection summary={summary} />

              <ReviewCostSection summary={summary} />

              <ReviewPolicySection summary={summary} />

              {mutation.isError && <Alert tone="error">{getErrorMessage(mutation.error)}</Alert>}

              <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-between">
                <Button variant="secondary" type="button" onClick={handleBackToSlotSelection}>
                  <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                  <span>Quay lại chọn khung giờ khác</span>
                </Button>

                <Button
                  variant="primary"
                  size="lg"
                  type="button"
                  loading={mutation.isPending}
                  onClick={handleConfirmBooking}
                >
                  <span>Xác nhận Đặt khám &amp; Cấp Số Thứ Tự</span>
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Button>
              </div>
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
