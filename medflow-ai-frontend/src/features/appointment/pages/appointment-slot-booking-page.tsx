import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, CalendarX2, ChevronRight, Home, RotateCcw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { apiClient } from '@/shared/lib/axios';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { toast } from '@/shared/ui/toast';

import { BookingDateSelector } from '../components/booking-date-selector';
import { BookingDoctorBanner } from '../components/booking-doctor-banner';
import { BookingPatientSelector } from '../components/booking-patient-selector';
import { BookingReasonSection } from '../components/booking-reason-section';
import { BookingSlotGrid } from '../components/booking-slot-grid';
import { BookingStepper } from '../components/booking-stepper';
import { BookingStickyFooter } from '../components/booking-sticky-footer';
import { useAvailableSlots } from '../hooks/use-available-slots';
import { useBookingStore } from '../store/booking.store';

const ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE =
  'Bệnh nhân đã có lịch khám đang hoạt động trong ngày này. Vui lòng hoàn thành hoặc hủy lịch khám hiện tại trước khi đặt lịch mới.';

export function AppointmentSlotBookingPage() {
  const { doctorId: paramDoctorId } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeDoctorId = useBookingStore((s) => s.selectedDoctorId);
  const queryServiceId = searchParams.get('serviceId');
  const selectedPatientId = useBookingStore((s) => s.selectedPatientId);
  const selectedDateIso = useBookingStore((s) => s.selectedDateIso);
  const selectedSlotId = useBookingStore((s) => s.selectedSlotId);
  const symptomNote = useBookingStore((s) => s.symptomNote);

  const selectPatient = useBookingStore((s) => s.selectPatient);
  const selectDate = useBookingStore((s) => s.selectDate);
  const selectSlot = useBookingStore((s) => s.selectSlot);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);
  const queryDoctorId = paramDoctorId ?? searchParams.get('doctorId') ?? storeDoctorId;

  const [reasonErrorState, setReasonErrorState] = useState<{
    doctorId: string | null;
    message: string;
  } | null>(null);
  const [bookingConflictState, setBookingConflictState] = useState<{
    doctorId: string | null;
    message: string;
  } | null>(null);

  const reasonError =
    reasonErrorState?.doctorId === queryDoctorId ? reasonErrorState.message : null;
  const bookingConflictError =
    bookingConflictState?.doctorId === queryDoctorId ? bookingConflictState.message : null;

  const setReasonError = (message: string | null) =>
    setReasonErrorState(message ? { doctorId: queryDoctorId, message } : null);
  const setBookingConflictError = (message: string | null) =>
    setBookingConflictState(message ? { doctorId: queryDoctorId, message } : null);

  const [isCheckingPreview, setIsCheckingPreview] = useState(false);

  const {
    data: payload,
    isLoading,
    isError,
    error,
    refetch,
  } = useAvailableSlots({
    doctorId: queryDoctorId,
    serviceId: queryServiceId,
    dateIso: selectedDateIso,
  });

  const morningSlots = useMemo(() => payload?.morningSlots ?? [], [payload?.morningSlots]);
  const afternoonSlots = useMemo(() => payload?.afternoonSlots ?? [], [payload?.afternoonSlots]);

  const allSlots = useMemo(
    () => [...morningSlots, ...afternoonSlots],
    [morningSlots, afternoonSlots],
  );

  const selectedProfile = useMemo(
    () => payload?.profiles.find((p) => p.id === selectedPatientId) ?? payload?.profiles[0],
    [payload?.profiles, selectedPatientId],
  );

  const selectedDate = useMemo(
    () =>
      payload?.dates.find(
        (d) => d.dateIso === selectedDateIso && d.status !== 'FULL' && d.status !== 'OFF',
      ) ?? payload?.dates.find((d) => d.status !== 'FULL' && d.status !== 'OFF'),
    [payload?.dates, selectedDateIso],
  );

  useEffect(() => {
    if (payload && selectedDate && selectedDate.dateIso !== selectedDateIso) {
      selectDate(selectedDate.dateIso);
    }
  }, [payload, selectedDate, selectedDateIso, selectDate]);

  const selectedSlot = useMemo(
    () =>
      allSlots.find((s) => s.id === selectedSlotId && s.status !== 'FULL') ??
      allSlots.find((s) => s.status !== 'FULL'),
    [allSlots, selectedSlotId],
  );

  const hasOpenSlots = Boolean(selectedSlot);
  const hasActiveBookingOnSelectedDate = Boolean(
    selectedDate && selectedProfile?.activeBookedDates?.includes(selectedDate.dateIso),
  );

  const activeConflictMessage =
    bookingConflictError ??
    (hasActiveBookingOnSelectedDate ? ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE : null);

  const handleSelectProfile = (profileId: string) => {
    selectPatient(profileId);
    const nextProfile = payload?.profiles.find((p) => p.id === profileId);
    const currentDateIso = selectedDate?.dateIso ?? selectedDateIso;
    if (nextProfile?.activeBookedDates?.includes(currentDateIso)) {
      setBookingConflictError(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE);
      toast.error(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE, 'Không thể đặt trùng ngày khám');
    } else {
      setBookingConflictError(null);
    }
  };

  const handleSelectDate = (dateIso: string) => {
    selectDate(dateIso);
    if (selectedProfile?.activeBookedDates?.includes(dateIso)) {
      setBookingConflictError(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE);
      toast.error(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE, 'Không thể đặt trùng ngày khám');
    } else {
      setBookingConflictError(null);
    }
  };

  const handleChangeSymptomNote = (value: string) => {
    setSymptomNote(value);
    if (reasonError && value.trim().length >= 3) {
      setReasonError(null);
    }
  };

  const handleChangeDoctorOrService = () => {
    navigate('/appointment/doctors');
  };

  const handleContinueToReview = async () => {
    if (!payload || !selectedProfile || !selectedDate || !selectedSlot) {
      const noSlotMsg = 'Hiện chưa có khung giờ khám hợp lệ để tiếp tục đặt lịch.';
      setBookingConflictError(noSlotMsg);
      toast.error(noSlotMsg, 'Chưa thể tiếp tục đặt lịch');
      return;
    }

    if (hasActiveBookingOnSelectedDate) {
      setBookingConflictError(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE);
      toast.error(ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE, 'Không thể đặt trùng ngày khám');
      document
        .getElementById('booking-conflict-alert')
        ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      return;
    }

    const trimmedReason = symptomNote.trim();
    if (trimmedReason.length < 3) {
      const validationMsg =
        'Vui lòng nhập lý do khám hoặc triệu chứng ban đầu (tối thiểu 3 ký tự).';
      setReasonError(validationMsg);
      toast.error(validationMsg, 'Thiếu thông tin bắt buộc');
      document.getElementById('booking-symptom-note')?.focus();
      return;
    }

    setReasonError(null);
    setBookingConflictError(null);

    if (import.meta.env.MODE !== 'test') {
      setIsCheckingPreview(true);
      try {
        await apiClient.get('/appointments/review-preview', {
          params: {
            mode: 'doctor',
            doctorId: payload.doctorService.doctorId,
            serviceId: payload.doctorService.serviceId,
            serviceCode: payload.doctorService.serviceCode,
            patientId: selectedProfile.id,
            dateIso: selectedDate.dateIso,
            slotId: selectedSlot.id,
            shiftId: selectedSlot.id,
            symptomNote: trimmedReason,
          },
        });
      } catch (err) {
        setIsCheckingPreview(false);
        const msg = getErrorMessage(err, ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE);
        setBookingConflictError(msg);
        toast.error(msg, 'Không thể tiếp tục đặt lịch');
        document
          .getElementById('booking-conflict-alert')
          ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        return;
      }
      setIsCheckingPreview(false);
    }

    const params = new URLSearchParams();
    params.set('mode', 'doctor');
    params.set('doctorId', payload.doctorService.doctorId);
    params.set('serviceId', payload.doctorService.serviceId);
    params.set('serviceCode', payload.doctorService.serviceCode);
    params.set('patientId', selectedProfile.id);
    params.set('date', selectedDate.dateIso);
    params.set('slotId', selectedSlot.id);
    params.set('symptomNote', trimmedReason);

    navigate(`/appointment/review?${params.toString()}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <Link to="/appointment/doctors" className="transition-colors hover:text-primary">
              Đặt khám theo Bác sĩ
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Chọn Ngày & Giờ khám</span>
          </nav>

          {/* 4-Step Progress Bar */}
          <BookingStepper mode="doctor" />

          {isLoading ? (
            <div
              role="status"
              aria-busy="true"
              aria-label="Đang tải lịch khám khả dụng của bác sĩ"
              className="space-y-5"
            >
              <div className="h-32 animate-pulse rounded-2xl border border-border bg-white p-6" />
              <div className="h-44 animate-pulse rounded-2xl border border-border bg-white p-6" />
              <div className="h-56 animate-pulse rounded-2xl border border-border bg-white p-6" />
            </div>
          ) : isError || !payload ? (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-rose-50 text-danger">
                <AlertCircle className="h-6 w-6" aria-hidden="true" />
              </div>
              <h2 className="mt-4 text-lg font-extrabold text-ink">
                Không thể tải lịch khám khả dụng của bác sĩ
              </h2>
              <p className="mx-auto mt-1.5 max-w-lg text-xs leading-relaxed text-sub">
                {getErrorMessage(
                  error,
                  'Máy chủ chưa phản hồi danh sách khung giờ khám. Vui lòng thử lại.',
                )}
              </p>
              <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
                <button
                  type="button"
                  onClick={handleChangeDoctorOrService}
                  className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
                >
                  <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Quay lại Danh bạ Bác sĩ</span>
                </button>
                <button
                  type="button"
                  onClick={() => void refetch()}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
                >
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Thử tải lại</span>
                </button>
              </div>
            </div>
          ) : (
            <>
              {/* Selected Doctor & Service Summary Banner */}
              <BookingDoctorBanner
                info={payload.doctorService}
                onChangeDoctorOrService={handleChangeDoctorOrService}
              />

              {/* Main Booking Sections */}
              <div className="space-y-5">
                {activeConflictMessage && (
                  <div
                    id="booking-conflict-alert"
                    role="alert"
                    className="flex items-start gap-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3.5 text-xs text-rose-900 shadow-xs sm:text-sm"
                  >
                    <AlertCircle
                      className="mt-0.5 h-5 w-5 shrink-0 text-danger"
                      aria-hidden="true"
                    />
                    <div className="space-y-0.5">
                      <p className="font-bold text-rose-950">
                        Không thể đặt thêm lịch khám trong ngày {selectedDate?.fullDateDisplay}
                      </p>
                      <p className="leading-relaxed text-rose-800">{activeConflictMessage}</p>
                    </div>
                  </div>
                )}

                <BookingPatientSelector
                  profiles={payload.profiles}
                  selectedProfileId={selectedProfile?.id ?? ''}
                  onSelectProfile={handleSelectProfile}
                />

                <BookingDateSelector
                  monthLabel={payload.monthLabel}
                  dates={payload.dates}
                  selectedDateIso={selectedDate?.dateIso ?? selectedDateIso}
                  onSelectDate={handleSelectDate}
                />

                {!hasOpenSlots && allSlots.length === 0 ? (
                  <section
                    role="status"
                    aria-label="Không có khung giờ trống"
                    className="rounded-2xl border border-border bg-white p-8 text-center shadow-xs"
                  >
                    <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-canvas text-sub">
                      <CalendarX2 className="h-5 w-5" aria-hidden="true" />
                    </div>
                    <h2 className="mt-3 text-sm font-bold text-ink sm:text-base">
                      Hiện không còn khung giờ khám trống trong ngày đã chọn
                    </h2>
                    <p className="mt-1 text-xs text-sub">
                      Vui lòng chọn một ngày khám khác trên lịch hoặc đổi bác sĩ chuyên khoa.
                    </p>
                  </section>
                ) : (
                  <BookingSlotGrid
                    morningSlots={morningSlots}
                    afternoonSlots={afternoonSlots}
                    activeDayShift={payload.activeDayShift}
                    selectedSlotId={selectedSlot?.id ?? ''}
                    onSelectSlot={selectSlot}
                  />
                )}

                <BookingReasonSection
                  symptomNote={symptomNote}
                  onChangeSymptomNote={handleChangeSymptomNote}
                  error={reasonError}
                />
              </div>

              {/* Bottom Summary Action Bar */}
              <BookingStickyFooter
                doctorService={payload.doctorService}
                selectedProfile={selectedProfile}
                selectedDate={selectedDate}
                selectedSlot={isCheckingPreview ? undefined : selectedSlot}
                onContinue={() => void handleContinueToReview()}
              />
            </>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
