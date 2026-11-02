import { Link, useNavigate } from 'react-router-dom';
import { ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { BookingEntryStepHeader } from '../components/booking-entry-step-header';
import { BookingEntrySummary } from '../components/booking-entry-summary';
import { BookingHospitalList } from '../components/booking-hospital-list';
import { BookingModeCard } from '../components/booking-mode-card';
import { useHospitals } from '../hooks/use-hospitals';
import { useBookingStore } from '../store/booking.store';
import type { BookingMode } from '../types/booking-entry.types';

export function BookingEntryPage() {
  const navigate = useNavigate();
  const { data: hospitals = [], isLoading, isError, refetch } = useHospitals();

  const selectedHospitalId = useBookingStore((s) => s.selectedHospitalId);
  const selectHospital = useBookingStore((s) => s.selectHospital);
  const selectBookingMode = useBookingStore((s) => s.selectBookingMode);

  const activeHospitals = hospitals.filter((h) => h.isActive);
  const resolvedHospitalId =
    selectedHospitalId && activeHospitals.some((h) => h.id === selectedHospitalId)
      ? selectedHospitalId
      : activeHospitals.length === 1
        ? activeHospitals[0].id
        : null;
  const resolvedHospital = hospitals.find((h) => h.id === resolvedHospitalId) ?? null;

  const handleSelectMode = (mode: BookingMode) => {
    if (!resolvedHospitalId) return;
    selectHospital(resolvedHospitalId);
    selectBookingMode(mode);
    const target =
      mode === 'DOCTOR'
        ? `/doctors?hospitalId=${encodeURIComponent(resolvedHospitalId)}`
        : `/services?hospitalId=${encodeURIComponent(resolvedHospitalId)}`;
    navigate(target);
  };

  const step1State = resolvedHospitalId ? 'done' : 'current';
  const step2State = resolvedHospitalId ? 'current' : 'upcoming';
  const step1Hint =
    !isLoading && !isError && hospitals.length > 0
      ? `${hospitals.length} cơ sở • ${activeHospitals.length} đang tiếp nhận`
      : undefined;
  const step2Hint = resolvedHospitalId
    ? 'Bấm vào một hình thức để tiếp tục'
    : 'Vui lòng chọn cơ sở khám ở Bước 1';

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav
            aria-label="Breadcrumb"
            className="mb-4 flex items-center gap-2 text-xs text-slate-500"
          >
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-sky-600">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-primary">Đặt lịch khám</span>
          </nav>

          {/* Heading */}
          <div className="mb-6">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
              Đặt lịch khám ngoại trú
            </h1>
            <p className="mt-1 text-sm text-sub">
              Chọn cơ sở khám và hình thức đặt lịch phù hợp — chỉ mất chưa đến 2 phút.
            </p>
          </div>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Left column: Step 1 + Step 2 */}
            <div className="space-y-6 lg:col-span-8">
              <section className="rounded-2xl border border-border bg-white p-5 shadow-sm">
                <BookingEntryStepHeader
                  step={1}
                  title="Chọn cơ sở khám"
                  hint={step1Hint}
                  state={step1State}
                />
                <div className="mt-4">
                  <BookingHospitalList
                    hospitals={hospitals}
                    isLoading={isLoading}
                    isError={isError}
                    onRetry={() => void refetch()}
                    selectedHospitalId={resolvedHospitalId}
                    onSelect={selectHospital}
                  />
                </div>
              </section>

              <section className="rounded-2xl border border-border bg-white p-5 shadow-sm">
                <BookingEntryStepHeader
                  step={2}
                  title="Chọn hình thức đặt khám"
                  hint={step2Hint}
                  state={step2State}
                />
                <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
                  <BookingModeCard
                    mode="DOCTOR"
                    disabled={!resolvedHospitalId}
                    onSelect={handleSelectMode}
                  />
                  <BookingModeCard
                    mode="SERVICE"
                    disabled={!resolvedHospitalId}
                    onSelect={handleSelectMode}
                  />
                </div>
              </section>
            </div>

            {/* Right column: summary */}
            <aside className="lg:col-span-4">
              <BookingEntrySummary hospital={resolvedHospital} />
            </aside>
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
