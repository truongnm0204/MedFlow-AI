import { useMemo } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { BookingDateSelector } from '../components/booking-date-selector';
import { BookingDoctorBanner } from '../components/booking-doctor-banner';
import { BookingPatientSelector } from '../components/booking-patient-selector';
import { BookingReasonSection } from '../components/booking-reason-section';
import { BookingSlotGrid } from '../components/booking-slot-grid';
import { BookingStepper } from '../components/booking-stepper';
import { BookingStickyFooter } from '../components/booking-sticky-footer';
import { getMockAvailableSlotsPayload } from '../data/slot-booking-mock';
import { useAvailableSlots } from '../hooks/use-available-slots';
import { useBookingStore } from '../store/booking.store';

export function AppointmentSlotBookingPage() {
  const { doctorId: paramDoctorId } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeDoctorId = useBookingStore((s) => s.selectedDoctorId);
  const storeServiceId = useBookingStore((s) => s.selectedServiceId);
  const selectedPatientId = useBookingStore((s) => s.selectedPatientId);
  const selectedDateIso = useBookingStore((s) => s.selectedDateIso);
  const selectedSlotId = useBookingStore((s) => s.selectedSlotId);
  const symptomNote = useBookingStore((s) => s.symptomNote);

  const selectPatient = useBookingStore((s) => s.selectPatient);
  const selectDate = useBookingStore((s) => s.selectDate);
  const selectSlot = useBookingStore((s) => s.selectSlot);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);

  const queryDoctorId = paramDoctorId ?? searchParams.get('doctorId') ?? storeDoctorId;
  const queryServiceId = searchParams.get('serviceId') ?? storeServiceId;

  const { data } = useAvailableSlots({
    doctorId: queryDoctorId,
    serviceId: queryServiceId,
    dateIso: selectedDateIso,
  });

  const payload = useMemo(
    () => data ?? getMockAvailableSlotsPayload(queryDoctorId, queryServiceId, selectedDateIso),
    [data, queryDoctorId, queryServiceId, selectedDateIso],
  );

  const allSlots = useMemo(
    () => [...payload.morningSlots, ...payload.afternoonSlots],
    [payload.morningSlots, payload.afternoonSlots],
  );

  const selectedProfile = useMemo(
    () => payload.profiles.find((p) => p.id === selectedPatientId) ?? payload.profiles[0],
    [payload.profiles, selectedPatientId],
  );

  const selectedDate = useMemo(
    () =>
      payload.dates.find((d) => d.dateIso === selectedDateIso) ??
      payload.dates.find((d) => d.status !== 'FULL' && d.status !== 'OFF'),
    [payload.dates, selectedDateIso],
  );

  const selectedSlot = useMemo(
    () =>
      allSlots.find((s) => s.id === selectedSlotId && s.status !== 'FULL') ??
      allSlots.find((s) => s.status !== 'FULL'),
    [allSlots, selectedSlotId],
  );

  const handleChangeDoctorOrService = () => {
    navigate('/appointment/doctors');
  };

  const handleContinueToReview = () => {
    const params = new URLSearchParams();
    params.set('mode', 'doctor');
    params.set('doctorId', payload.doctorService.doctorId);
    params.set('serviceCode', payload.doctorService.serviceCode);
    if (selectedProfile) params.set('patientId', selectedProfile.id);
    if (selectedDate) params.set('date', selectedDate.dateIso);
    if (selectedSlot) params.set('slotId', selectedSlot.id);

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

          {/* Selected Doctor & Service Summary Banner */}
          <BookingDoctorBanner
            info={payload.doctorService}
            onChangeDoctorOrService={handleChangeDoctorOrService}
          />

          {/* Main Booking Sections */}
          <div className="space-y-5">
            <BookingPatientSelector
              profiles={payload.profiles}
              selectedProfileId={selectedProfile?.id ?? 'pat-self'}
              onSelectProfile={selectPatient}
            />

            <BookingDateSelector
              monthLabel={payload.monthLabel}
              dates={payload.dates}
              selectedDateIso={selectedDate?.dateIso ?? '2026-10-13'}
              onSelectDate={selectDate}
            />

            <BookingSlotGrid
              morningSlots={payload.morningSlots}
              afternoonSlots={payload.afternoonSlots}
              activeDayShift={payload.activeDayShift}
              selectedSlotId={selectedSlot?.id ?? 'slot-0900'}
              onSelectSlot={selectSlot}
            />

            <BookingReasonSection symptomNote={symptomNote} onChangeSymptomNote={setSymptomNote} />
          </div>

          {/* Bottom Summary Action Bar */}
          <BookingStickyFooter
            doctorService={payload.doctorService}
            selectedProfile={selectedProfile}
            selectedDate={selectedDate}
            selectedSlot={selectedSlot}
            onContinue={handleContinueToReview}
          />
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
