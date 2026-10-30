import { useMemo } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { BookingDateSelector } from '../components/booking-date-selector';
import { BookingPatientSelector } from '../components/booking-patient-selector';
import { BookingReasonSection } from '../components/booking-reason-section';
import { BookingServiceBanner } from '../components/booking-service-banner';
import { BookingServiceShiftSelector } from '../components/booking-service-shift-selector';
import { BookingServiceStickyFooter } from '../components/booking-service-sticky-footer';
import { BookingStepper } from '../components/booking-stepper';
import { getMockServiceAvailableShiftsPayload } from '../data/slot-booking-mock';
import { useServiceAvailableShifts } from '../hooks/use-available-slots';
import { useBookingStore } from '../store/booking.store';

export function ServiceSlotBookingPage() {
  const { serviceId: paramServiceId } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const storeServiceId = useBookingStore((s) => s.selectedServiceId);
  const selectedPatientId = useBookingStore((s) => s.selectedPatientId);
  const selectedDateIso = useBookingStore((s) => s.selectedDateIso);
  const selectedShiftId = useBookingStore((s) => s.selectedShiftId);
  const symptomNote = useBookingStore((s) => s.symptomNote);

  const selectPatient = useBookingStore((s) => s.selectPatient);
  const selectDate = useBookingStore((s) => s.selectDate);
  const selectShift = useBookingStore((s) => s.selectShift);
  const setSymptomNote = useBookingStore((s) => s.setSymptomNote);

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
      payload.dates.find((d) => d.dateIso === selectedDateIso) ??
      payload.dates.find((d) => d.status !== 'FULL' && d.status !== 'OFF'),
    [payload.dates, selectedDateIso],
  );

  const selectedShift = useMemo(
    () =>
      payload.shifts.find((s) => s.id === selectedShiftId && s.status !== 'FULL') ??
      payload.shifts.find((s) => s.status !== 'FULL'),
    [payload.shifts, selectedShiftId],
  );

  const handleChangeService = () => {
    navigate('/appointment/services');
  };

  const handleContinueToReview = () => {
    const params = new URLSearchParams();
    params.set('mode', 'service');
    params.set('serviceId', payload.serviceInfo.serviceId);
    params.set('serviceCode', payload.serviceInfo.serviceCode);
    if (selectedProfile) params.set('patientId', selectedProfile.id);
    if (selectedDate) params.set('date', selectedDate.dateIso);
    if (selectedShift) params.set('shiftId', selectedShift.id);

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

            <BookingServiceShiftSelector
              shifts={payload.shifts}
              selectedShiftId={selectedShift?.id ?? 'shift-morning'}
              onSelectShift={selectShift}
            />

            <BookingReasonSection symptomNote={symptomNote} onChangeSymptomNote={setSymptomNote} />
          </div>

          {/* Bottom Summary Action Bar (Service Mode) */}
          <BookingServiceStickyFooter
            serviceInfo={payload.serviceInfo}
            selectedProfile={selectedProfile}
            selectedDate={selectedDate}
            selectedShift={selectedShift}
            onContinue={handleContinueToReview}
          />
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
