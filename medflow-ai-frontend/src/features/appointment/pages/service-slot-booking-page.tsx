import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, CalendarX2, ChevronRight, Home, RotateCcw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { apiClient } from '@/shared/lib/axios';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { toast } from '@/shared/ui/toast';

import { BookingDateSelector } from '../components/booking-date-selector';
import { BookingPatientSelector } from '../components/booking-patient-selector';
import { BookingReasonSection } from '../components/booking-reason-section';
import { BookingServiceBanner } from '../components/booking-service-banner';
import { BookingServiceStickyFooter } from '../components/booking-service-sticky-footer';
import { BookingSlotGrid } from '../components/booking-slot-grid';
import { BookingStepper } from '../components/booking-stepper';
import { useServiceAvailableShifts } from '../hooks/use-available-slots';
import { useBookingStore } from '../store/booking.store';

const ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE =
  'Bệnh nhân đã có lịch khám đang hoạt động trong ngày này. Vui lòng hoàn thành hoặc hủy lịch khám hiện tại trước khi đặt lịch mới.';

export function ServiceSlotBookingPage() {
  const { serviceId: paramServiceId } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeServiceId = useBookingStore((s) => s.selectedServiceId);
  const queryPatientId = searchParams.get('patientId')?.trim() ?? '';
  const selectedPatientId = useBookingStore((s) => s.selectedPatientId);
  const selectedDateIso = useBookingStore((s) => s.selectedDateIso);
  const selectedSlotId = useBookingStore((s) => s.selectedSlotId);
  const selectedShiftId = useBookingStore((s) => s.selectedShiftId);
  const symptomNote = useBookingStore((s) => s.symptomNote);

  const selectPatient = useBookingStore((s) => s.selectPatient);
  const selectDate = useBookingStore((s) => s.selectDate);
  const selectSlot = useBookingStore((s) => s.selectSlot);
  const selectShift = useBookingStore((s) => s.selectShift);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);

  const [reasonError, setReasonError] = useState<string | null>(null);
  const [bookingConflictError, setBookingConflictError] = useState<string | null>(null);
  const [isCheckingPreview, setIsCheckingPreview] = useState(false);
  const queryServiceId = paramServiceId ?? searchParams.get('serviceId') ?? storeServiceId;

  const {
    data: payload,
    isLoading,
    isError,
    error,
    refetch,
  } = useServiceAvailableShifts({
    serviceId: queryServiceId,
    dateIso: selectedDateIso,
  });

  const selectedProfile = useMemo(() => {
    if (
      /^[1-9]\d*$/.test(queryPatientId) &&
      payload?.profiles.some((p) => p.id === queryPatientId) &&
      selectedPatientId === 'pat-self'
    ) {
      return payload.profiles.find((p) => p.id === queryPatientId);
    }
    return payload?.profiles.find((p) => p.id === selectedPatientId) ?? payload?.profiles[0];
  }, [payload?.profiles, queryPatientId, selectedPatientId]);

  useEffect(() => {
    if (
      payload &&
      /^[1-9]\d*$/.test(queryPatientId) &&
      payload.profiles.some((p) => p.id === queryPatientId) &&
      selectedPatientId === 'pat-self'
    ) {
      selectPatient(queryPatientId);
    }
  }, [payload, queryPatientId, selectPatient, selectedPatientId]);

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

  const morningSlots = useMemo(() => payload?.morningSlots ?? [], [payload?.morningSlots]);
  const afternoonSlots = useMemo(() => payload?.afternoonSlots ?? [], [payload?.afternoonSlots]);
  const shifts = useMemo(() => payload?.shifts ?? [], [payload?.shifts]);
  const activeDayShift = payload?.activeDayShift;

  const allSlots = useMemo(
    () => [...morningSlots, ...afternoonSlots],
    [morningSlots, afternoonSlots],
  );

  const selectedSlot = useMemo(
    () =>
      allSlots.find((s) => s.id === selectedSlotId && s.status !== 'FULL') ??
      allSlots.find((s) => s.status !== 'FULL'),
    [allSlots, selectedSlotId],
  );

  const selectedShift = useMemo(
    () =>
      allSlots.length > 0
        ? undefined
        : (shifts.find((sh) => sh.id === selectedShiftId && sh.status !== 'FULL') ??
          shifts.find((sh) => sh.status !== 'FULL')),
    [allSlots.length, shifts, selectedShiftId],
  );

  const hasValidSlotOrShift = Boolean(selectedSlot || selectedShift);
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

  const handleChangeService = () => {
    navigate('/appointment/services');
  };

  const handleContinueToReview = async () => {
    const activeChoiceId = selectedSlot?.id ?? selectedShift?.id;
    if (!payload || !selectedProfile || !selectedDate || !activeChoiceId) {
      const noSlotMsg = 'Hiện chưa có ca hoặc khung giờ khám hợp lệ để tiếp tục đặt lịch.';
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
            mode: 'service',
            serviceId: payload.serviceInfo.serviceId,
            serviceCode: payload.serviceInfo.serviceCode,
            patientId: selectedProfile.id,
            dateIso: selectedDate.dateIso,
            slotId: activeChoiceId,
            shiftId: activeChoiceId,
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
    params.set('mode', 'service');
    params.set('serviceId', payload.serviceInfo.serviceId);
    params.set('serviceCode', payload.serviceInfo.serviceCode);
    params.set('patientId', selectedProfile.id);
    params.set('date', selectedDate.dateIso);
    params.set('slotId', activeChoiceId);
    params.set('shiftId', activeChoiceId);
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
            <Link to="/appointment/services" className="transition-colors hover:text-primary">
              Danh mục Dịch vụ Khám
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Chọn Ngày & Ca khám</span>
          </nav>

          {/* 4-Step Progress Bar (Service Mode) */}
          <BookingStepper mode="service" />

          {isLoading ? (
            <div
              role="status"
              aria-busy="true"
              aria-label="Đang tải lịch ca khám dịch vụ"
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
                Không thể tải lịch ca khám của dịch vụ
              </h2>
              <p className="mx-auto mt-1.5 max-w-lg text-xs leading-relaxed text-sub">
                {getErrorMessage(
                  error,
                  'Máy chủ chưa phản hồi danh sách ca khám dịch vụ. Vui lòng thử lại.',
                )}
              </p>
              <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
                <button
                  type="button"
                  onClick={handleChangeService}
                  className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
                >
                  <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Quay lại Danh mục Dịch vụ</span>
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
              {/* Selected Service Only Summary Banner (No Doctor) */}
              <BookingServiceBanner
                serviceInfo={payload.serviceInfo}
                onChangeService={handleChangeService}
              />

              {/* Main Service Booking Sections */}
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

                {allSlots.length > 0 ? (
                  <BookingSlotGrid
                    morningSlots={morningSlots}
                    afternoonSlots={afternoonSlots}
                    activeDayShift={activeDayShift}
                    selectedSlotId={selectedSlot?.id ?? ''}
                    onSelectSlot={selectSlot}
                  />
                ) : shifts.length > 0 && hasValidSlotOrShift ? (
                  <section
                    aria-labelledby="service-shifts-heading"
                    className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6 space-y-4"
                  >
                    <h2
                      id="service-shifts-heading"
                      className="text-sm font-bold text-ink sm:text-base"
                    >
                      3. Chọn Ca Khám Trong Ngày
                    </h2>
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                      {shifts.map((shiftOption) => {
                        const isFull = shiftOption.status === 'FULL';
                        const isSelected = selectedShift?.id === shiftOption.id && !isFull;
                        return (
                          <button
                            key={shiftOption.id}
                            type="button"
                            disabled={isFull}
                            aria-pressed={isSelected}
                            onClick={() => selectShift(shiftOption.id)}
                            className={`rounded-xl border p-4 text-left transition-colors ${
                              isSelected
                                ? 'border-primary bg-sky-50 text-ink'
                                : isFull
                                  ? 'cursor-not-allowed border-border/70 bg-canvas/80 text-sub opacity-60'
                                  : 'border-border bg-white text-ink hover:border-sky-300'
                            }`}
                          >
                            <div className="flex items-center justify-between gap-2">
                              <span className="text-xs font-extrabold">{shiftOption.title}</span>
                              <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700">
                                {shiftOption.badgeText}
                              </span>
                            </div>
                            <p className="mt-1 text-xs font-semibold text-primary">
                              {shiftOption.timeRange}
                            </p>
                            <p className="mt-1 text-[11px] text-sub">{shiftOption.description}</p>
                          </button>
                        );
                      })}
                    </div>
                  </section>
                ) : (
                  <section
                    role="status"
                    aria-label="Không có ca khám trống"
                    className="rounded-2xl border border-border bg-white p-8 text-center shadow-xs"
                  >
                    <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full bg-canvas text-sub">
                      <CalendarX2 className="h-5 w-5" aria-hidden="true" />
                    </div>
                    <h2 className="mt-3 text-sm font-bold text-ink sm:text-base">
                      Hiện không còn ca hoặc khung giờ khám trống trong ngày đã chọn
                    </h2>
                    <p className="mt-1 text-xs text-sub">
                      Vui lòng chọn ngày khám khác hoặc đổi dịch vụ chuyên khoa.
                    </p>
                  </section>
                )}

                <BookingReasonSection
                  symptomNote={symptomNote}
                  onChangeSymptomNote={handleChangeSymptomNote}
                  error={reasonError}
                />
              </div>

              {/* Bottom Summary Action Bar (Service Mode) */}
              <BookingServiceStickyFooter
                serviceInfo={payload.serviceInfo}
                selectedProfile={selectedProfile}
                selectedDate={selectedDate}
                selectedSlot={isCheckingPreview ? undefined : selectedSlot}
                selectedShift={isCheckingPreview ? undefined : selectedShift}
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
