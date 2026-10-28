import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Calendar, ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { DoctorFilterBar } from '../components/doctor-filter-bar';
import { DoctorGrid } from '../components/doctor-grid';
import { DoctorPagination } from '../components/doctor-pagination';
import { DoctorSearchBar } from '../components/doctor-search-bar';
import { DoctorSpecialtyTabs } from '../components/doctor-specialty-tabs';
import { useDoctorSearch } from '../hooks/use-doctor-search';
import type { DoctorFilterParams } from '../types/doctor-search.types';

export function DoctorSearchPage() {
  const [filters, setFilters] = useState<DoctorFilterParams>({
    keyword: '',
    specialty: 'ALL',
    degree: 'ALL',
    hasScheduleToday: false,
    hasScheduleThisWeek: false,
    page: 1,
    pageSize: 6,
  });

  const { data, isLoading } = useDoctorSearch(filters);

  const doctors = data?.items ?? [];
  const total = data?.total ?? 0;
  const page = data?.page ?? 1;
  const totalPages = data?.totalPages ?? 1;

  const handleSearch = (keyword: string) => {
    setFilters((prev) => ({ ...prev, keyword, page: 1 }));
  };

  const handleSelectSpecialty = (specialty: string) => {
    setFilters((prev) => ({ ...prev, specialty, page: 1 }));
  };

  const handleSelectDegree = (degree: string) => {
    setFilters((prev) => ({ ...prev, degree, page: 1 }));
  };

  const handleToggleScheduleToday = () => {
    setFilters((prev) => ({
      ...prev,
      hasScheduleToday: !prev.hasScheduleToday,
      page: 1,
    }));
  };

  const handleToggleScheduleThisWeek = () => {
    setFilters((prev) => ({
      ...prev,
      hasScheduleThisWeek: !prev.hasScheduleThisWeek,
      page: 1,
    }));
  };

  const handleResetFilters = () => {
    setFilters({
      keyword: '',
      specialty: 'ALL',
      degree: 'ALL',
      hasScheduleToday: false,
      hasScheduleThisWeek: false,
      page: 1,
      pageSize: 6,
    });
  };

  const handlePageChange = (newPage: number) => {
    setFilters((prev) => ({ ...prev, page: newPage }));
    window.scrollTo({ top: 180, behavior: 'smooth' });
  };

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
            <span className="text-slate-500">Đặt khám</span>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-sky-600">Danh bạ Bác sĩ (SCR-APT-01)</span>
          </nav>

          {/* Heading Row */}
          <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
              Danh bạ Bác sĩ Chuyên khoa Ngoại trú
            </h1>

            <div className="inline-flex w-fit items-center gap-2 rounded-xl border border-sky-100 bg-sky-50/70 px-3.5 py-1.5 text-xs text-slate-600 shadow-xs">
              <Calendar className="h-4 w-4 text-sky-600" aria-hidden="true" />
              <span>
                Cập nhật ca trực:{' '}
                <strong className="font-semibold text-slate-800">Hôm nay, 24/10/2026</strong>
              </span>
            </div>
          </div>

          {/* Search & Filter Card */}
          <section
            aria-label="Bộ lọc tìm kiếm bác sĩ"
            className="mb-7 rounded-2xl border border-slate-200/90 bg-white p-5 shadow-sm space-y-4"
          >
            <DoctorSearchBar initialKeyword={filters.keyword} onSearch={handleSearch} />

            <DoctorSpecialtyTabs
              selectedSpecialty={filters.specialty ?? 'ALL'}
              onSelectSpecialty={handleSelectSpecialty}
            />

            <DoctorFilterBar
              selectedDegree={filters.degree ?? 'ALL'}
              hasScheduleToday={Boolean(filters.hasScheduleToday)}
              hasScheduleThisWeek={Boolean(filters.hasScheduleThisWeek)}
              onSelectDegree={handleSelectDegree}
              onToggleScheduleToday={handleToggleScheduleToday}
              onToggleScheduleThisWeek={handleToggleScheduleThisWeek}
              onResetFilters={handleResetFilters}
            />
          </section>

          {/* Doctor List Results */}
          <section aria-label="Danh sách bác sĩ">
            <DoctorGrid
              doctors={doctors}
              isLoading={isLoading}
              onResetFilters={handleResetFilters}
            />
          </section>

          {/* Pagination */}
          {!isLoading && doctors.length > 0 && (
            <div className="mt-8">
              <DoctorPagination
                currentCount={doctors.length}
                total={total}
                page={page}
                totalPages={totalPages}
                onPageChange={handlePageChange}
              />
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
