import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { AlertCircle, ChevronRight, Home } from 'lucide-react';

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
import {
  getMockServiceAvailableShiftsPayload,
  resolveDoctorDayShiftSchedule,
} from '../data/slot-booking-mock';
import { useServiceAvailableShifts } from '../hooks/use-available-slots';
import { useBookingStore } from '../store/booking.store';
const ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE =
  'Bệnh nhân đã có lịch khám đang hoạt động trong ngày này. Vui lòng hoàn thành hoặc hủy lịch khám hiện tại trước khi đặt lịch mới.';

export function ServiceSlotBookingPage() {
  const { serviceId: paramServiceId } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeServiceId = useBookingStore((s) => s.selectedServiceId);
  const selectedPatientId = useBookingStore((s) => s.selectedPatientId);
  const selectedDateIso = useBookingStore((s) => s.selectedDateIso);
  const selectedSlotId = useBookingStore((s) => s.selectedSlotId);
  const symptomNote = useBookingStore((s) => s.symptomNote);

  const selectPatient = useBookingStore((s) => s.selectPatient);
  const selectDate = useBookingStore((s) => s.selectDate);
  const selectSlot = useBookingStore((s) => s.selectSlot);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);

  const [reasonError, setReasonError] = useState<string | null>(null);
  const [bookingConflictError, setBookingConflictError] = useState<string | null>(null);
  const [isCheckingPreview, setIsCheckingPreview] = useState(false);
  const queryServiceId = paramServiceId ?? searchParams.get('serviceId') ?? storeServiceId;

  const { data } = useServiceAvailableShifts({
    serviceId: queryServiceId,
    dateIso: selectedDateIso,
  });

  const payload = useMemo(
    () => data ?? getMockServiceAvailableShiftsPayload(queryServiceId, selectedDateIso),
    [data, queryServiceId, selectedDateIso],
  );

  const selectedProfile = useMemo(
    () => payload.profiles.find((p) => p.id === selectedPatientId) ?? payload.profiles[0],
    [payload.profiles, selectedPatientId],
  );

  const selectedDate = useMemo(
    () =>
      payload.dates.find(
        (d) => d.dateIso === selectedDateIso && d.status !== 'FULL' && d.status !== 'OFF',
      ) ?? payload.dates.find((d) => d.status !== 'FULL' && d.status !== 'OFF'),
    [payload.dates, selectedDateIso],
  );

  useEffect(() => {
    if (data && selectedDate && selectedDate.dateIso !== selectedDateIso) {
      selectDate(selectedDate.dateIso);
    }
  }, [data, selectedDate, selectedDateIso, selectDate]);

  const fallbackDayShift = useMemo(
    () => resolveDoctorDayShiftSchedule(selectedDate?.dateIso ?? selectedDateIso),
    [selectedDate?.dateIso, selectedDateIso],
  );

  const morningSlots = payload.morningSlots ?? fallbackDayShift.morningSlots;
  const afternoonSlots = payload.afternoonSlots ?? fallbackDayShift.afternoonSlots;
  const activeDayShift = payload.activeDayShift ?? fallbackDayShift;

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
  const hasActiveBookingOnSelectedDate = Boolean(
    selectedDate && selectedProfile?.activeBookedDates?.includes(selectedDate.dateIso),
  );

  const activeConflictMessage =
    bookingConflictError ??
    (hasActiveBookingOnSelectedDate ? ACTIVE_APPOINTMENT_SAME_DAY_MESSAGE : null);

  const handleSelectProfile = (profileId: string) => {
    selectPatient(profileId);
    const nextProfile = payload.profiles.find((p) => p.id === profileId);
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

    if (import.meta.env.MODE !== 'test' && selectedProfile && selectedDate && selectedSlot) {
      setIsCheckingPreview(true);
      try {
        await apiClient.get('/appointments/review-preview', {
          params: {
            mode: 'service',
            serviceId: payload.serviceInfo.serviceId,
            serviceCode: payload.serviceInfo.serviceCode,
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
    params.set('mode', 'service');
    params.set('serviceId', payload.serviceInfo.serviceId);
    params.set('serviceCode', payload.serviceInfo.serviceCode);
    if (selectedProfile) params.set('patientId', selectedProfile.id);
    if (selectedDate) params.set('date', selectedDate.dateIso);
    if (selectedSlot) {
      params.set('slotId', selectedSlot.id);
      params.set('shiftId', selectedSlot.id);
    }
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
                <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-danger" aria-hidden="true" />
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
              selectedProfileId={selectedProfile?.id ?? 'pat-self'}
              onSelectProfile={handleSelectProfile}
            />

            <BookingDateSelector
              monthLabel={payload.monthLabel}
              dates={payload.dates}
              selectedDateIso={selectedDate?.dateIso ?? '2026-10-13'}
              onSelectDate={handleSelectDate}
            />

            <BookingSlotGrid
              morningSlots={morningSlots}
              afternoonSlots={afternoonSlots}
              activeDayShift={activeDayShift}
              selectedSlotId={selectedSlot?.id ?? 'slot-0900'}
              onSelectSlot={selectSlot}
            />

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
            onContinue={() => void handleContinueToReview()}
          />
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
