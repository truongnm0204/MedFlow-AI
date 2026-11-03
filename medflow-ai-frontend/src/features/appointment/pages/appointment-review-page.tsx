import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, ArrowRight, ChevronRight, Loader2 } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { toast } from '@/shared/ui/toast';

import { ReviewFeeBreakdownCard } from '../components/review-fee-breakdown-card';
import { ReviewHeroStepperCard } from '../components/review-hero-stepper-card';
import { ReviewPatientInfoCard } from '../components/review-patient-info-card';
import { ReviewReceptionPolicyCard } from '../components/review-reception-policy-card';
import { ReviewScheduleLocationCard } from '../components/review-schedule-location-card';
import { getMockAppointmentReviewPayload } from '../data/review-booking-mock';
import {
  useAppointmentReviewPreview,
  useConfirmAppointmentBooking,
} from '../hooks/use-review-booking';
import { confirmBookingSchema } from '../schemas/review-booking.schema';
import { useBookingStore } from '../store/booking.store';
import type { ReviewBookingMode } from '../types/review-booking.types';

export function AppointmentReviewPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeDoctorId = useBookingStore((s) => s.selectedDoctorId);
  const storeServiceId = useBookingStore((s) => s.selectedServiceId);
  const storePatientId = useBookingStore((s) => s.selectedPatientId);
  const storeDateIso = useBookingStore((s) => s.selectedDateIso);
  const storeSlotId = useBookingStore((s) => s.selectedSlotId);
  const storeShiftId = useBookingStore((s) => s.selectedShiftId);
  const storeSymptomNote = useBookingStore((s) => s.symptomNote);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);
  const resetBooking = useBookingStore((s) => s.reset);

  const [submitError, setSubmitError] = useState<string | null>(null);

  const mode: ReviewBookingMode = searchParams.get('mode') === 'service' ? 'service' : 'doctor';
  const doctorId = searchParams.get('doctorId') ?? storeDoctorId ?? 'doc-01';
  const serviceId = searchParams.get('serviceId') ?? storeServiceId;
  const serviceCode = searchParams.get('serviceCode') ?? 'DV-TH-01';
  const patientId = searchParams.get('patientId') ?? storePatientId ?? 'pat-self';
  const dateIso = searchParams.get('date') ?? storeDateIso ?? '2026-10-13';
  const slotId = searchParams.get('slotId') ?? storeSlotId ?? 'slot-0900';
  const shiftId = searchParams.get('shiftId') ?? storeShiftId ?? 'shift-morning';
  const querySymptomNote = searchParams.get('symptomNote')?.trim() ?? '';
  const currentSymptomNote = storeSymptomNote.trim() || querySymptomNote;

  const queryParams = useMemo(
    () => ({
      mode,
      doctorId,
      serviceId,
      serviceCode,
      patientId,
      dateIso,
      slotId,
      shiftId,
      symptomNote: currentSymptomNote,
    }),
    [
      mode,
      doctorId,
      serviceId,
      serviceCode,
      patientId,
      dateIso,
      slotId,
      shiftId,
      currentSymptomNote,
    ],
  );

  const { data, isLoading, isError, error } = useAppointmentReviewPreview(queryParams);
  const confirmMutation = useConfirmAppointmentBooking();

  const reviewPayload = useMemo(
    () => data ?? getMockAppointmentReviewPayload(queryParams),
    [data, queryParams],
  );

  const effectiveSymptomNote = currentSymptomNote;

  const handleBackToSlotSelection = () => {
    if (mode === 'service') {
      const targetServiceId = reviewPayload.scheduleLocation.serviceId || 'srv-th-01';
      navigate(`/appointment/services/${encodeURIComponent(targetServiceId)}/booking`);
      return;
    }

    const targetDoctorId = reviewPayload.scheduleLocation.doctorId || 'doc-01';
    navigate(`/appointment/doctors/${encodeURIComponent(targetDoctorId)}/booking`);
  };

  const handleConfirmBooking = () => {
    setSubmitError(null);

    const candidatePayload = {
      mode,
      patientId: reviewPayload.patientInfo.patientId,
      doctorId: reviewPayload.scheduleLocation.doctorId,
      serviceId: reviewPayload.scheduleLocation.serviceId,
      serviceCode: reviewPayload.scheduleLocation.serviceCode,
      dateIso: reviewPayload.scheduleLocation.dateIso,
      slotOrShiftId: reviewPayload.scheduleLocation.slotOrShiftId,
      symptomNote: effectiveSymptomNote,
    };

    const parsed = confirmBookingSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const validationMsg =
        parsed.error.issues[0]?.message ?? 'Thông tin đặt lịch chưa hợp lệ. Vui lòng kiểm tra lại.';
      setSubmitError(validationMsg);
      toast.error(validationMsg, 'Đặt lịch không thành công');
      return;
    }

    confirmMutation.mutate(parsed.data, {
      onSuccess: (response) => {
        resetBooking();
        toast.success('Đặt lịch khám thành công! Hệ thống đã cấp số thứ tự giữ trước.');
        navigate(`/appointment/confirmation/${encodeURIComponent(response.appointmentId)}`, {
          replace: true,
        });
      },
      onError: (err) => {
        const errMsg = getErrorMessage(err);
        setSubmitError(errMsg);
        toast.error(errMsg, 'Đặt lịch không thành công');
      },
    });
  };

  const bookingCatalogPath = mode === 'service' ? '/appointment/services' : '/appointment/doctors';

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 py-8">
        <div className="mx-auto max-w-4xl space-y-6 px-4 sm:px-6 lg:px-8">
          {/* Breadcrumbs */}
          <nav
            aria-label="Breadcrumb"
            className="flex items-center gap-2 border-b border-border/80 pb-2.5 text-xs text-sub"
          >
            <Link to="/" className="font-medium transition-colors hover:text-primary">
              Trang chủ
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <Link
              to={bookingCatalogPath}
              className="font-medium transition-colors hover:text-primary"
            >
              Đặt khám
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary-dark">Soát xét Thông tin Đặt hẹn</span>
          </nav>

          {/* Hero Guidance & 4-Step Stepper */}
          <ReviewHeroStepperCard mode={mode} />

          {/* Error Banner (Query or Mutation) */}
          {(isError || submitError) && (
            <div
              role="alert"
              className="flex items-start gap-3 rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"
            >
              <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-red-600" aria-hidden="true" />
              <div className="space-y-1">
                <p className="font-bold">Không thể hoàn tất xác nhận đặt khám</p>
                <p>{submitError ?? getErrorMessage(error)}</p>
              </div>
            </div>
          )}

          {isLoading ? (
            <div
              aria-busy="true"
              aria-label="Đang tải thông tin soát xét đặt lịch"
              className="space-y-6"
            >
              <div className="h-64 animate-pulse rounded-2xl border border-border bg-white p-6" />
              <div className="h-64 animate-pulse rounded-2xl border border-border bg-white p-6" />
              <div className="h-56 animate-pulse rounded-2xl border border-border bg-white p-6" />
            </div>
          ) : (
            <>
              {/* Card 1: Patient EHR Info */}
              <ReviewPatientInfoCard
                patientInfo={reviewPayload.patientInfo}
                symptomNote={effectiveSymptomNote}
                onUpdateSymptomNote={setSymptomNote}
              />

              {/* Card 2: Schedule & Location */}
              <ReviewScheduleLocationCard scheduleLocation={reviewPayload.scheduleLocation} />

              {/* Card 3: Listed Fee Breakdown */}
              <ReviewFeeBreakdownCard feeSummary={reviewPayload.feeSummary} />

              {/* Card 4: Reception & Pre-allocated Queue Policy */}
              <ReviewReceptionPolicyCard policy={reviewPayload.receptionPolicy} />

              {/* Bottom Navigation & Primary Action */}
              <div className="flex flex-col-reverse items-stretch justify-between gap-4 pb-8 pt-4 sm:flex-row sm:items-center">
                <button
                  type="button"
                  onClick={handleBackToSlotSelection}
                  className="inline-flex items-center justify-center gap-2 rounded-xl border border-slate-300 bg-white px-6 py-3.5 text-sm font-medium text-ink shadow-2xs transition-colors hover:bg-canvas focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                  <span>Quay lại chọn khung giờ khác</span>
                </button>

                <button
                  type="button"
                  disabled={confirmMutation.isPending || isError}
                  onClick={handleConfirmBooking}
                  className="inline-flex items-center justify-center gap-2 rounded-xl bg-primary px-8 py-3.5 text-base font-bold text-white shadow-lg shadow-primary/25 transition-colors hover:bg-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {confirmMutation.isPending ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                      <span>Đang xác nhận & cấp STT...</span>
                    </>
                  ) : (
                    <>
                      <span>Xác nhận Đặt khám & Cấp Số Thứ Tự</span>
                      <ArrowRight className="h-4 w-4" aria-hidden="true" />
                    </>
                  )}
                </button>
              </div>
            </>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
