import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { ServiceCategoryTabs } from '../components/service-category-tabs';
import { ServiceDetailModal } from '../components/service-detail-modal';
import { ServiceFilterBar } from '../components/service-filter-bar';
import { ServiceGrid } from '../components/service-grid';
import { ServicePagination } from '../components/service-pagination';
import { ServiceSearchBar } from '../components/service-search-bar';
import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';
import { useExaminationServicesQuery } from '../hooks/use-service-search';
import { useBookingStore } from '../store/booking.store';
import type {
  ExaminationService,
  ServiceCategory,
  ServiceSearchParams,
} from '../types/service-search.types';

export function ExaminationServiceCatalogPage() {
  const { serviceId } = useParams();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const selectService = useBookingStore((s) => s.selectService);

  const [selectedService, setSelectedService] = useState<ExaminationService | null>(null);

  // Derive active service from state or URL parameter
  const targetServiceId = serviceId ?? searchParams.get('serviceId');
  const activeService = useMemo(() => {
    if (selectedService) return selectedService;
    if (targetServiceId) {
      return (
        MOCK_EXAMINATION_SERVICES.find(
          (s) =>
            s.id === targetServiceId ||
            s.serviceCode.toLowerCase() === targetServiceId.toLowerCase(),
        ) ?? null
      );
    }
    return null;
  }, [selectedService, targetServiceId]);

  const [filters, setFilters] = useState<ServiceSearchParams>({
    query: '',
    category: 'all',
    department: 'all',
    priceDurationRange: 'all',
    page: 1,
    pageSize: 6,
  });

  const { data, isLoading } = useExaminationServicesQuery(filters);

  const services = data?.data ?? [];
  const total = data?.total ?? 0;
  const page = data?.page ?? 1;
  const totalPages = data?.totalPages ?? 1;

  const hasActiveFilters = Boolean(
    filters.query ||
    (filters.category && filters.category !== 'all') ||
    (filters.department && filters.department !== 'all') ||
    (filters.priceDurationRange && filters.priceDurationRange !== 'all'),
  );

  const handleSearch = (query: string) => {
    setFilters((prev) => ({ ...prev, query, page: 1 }));
  };

  const handleSelectCategory = (category: ServiceCategory) => {
    setFilters((prev) => ({ ...prev, category, page: 1 }));
  };

  const handleSelectDepartment = (department: string) => {
    setFilters((prev) => ({ ...prev, department, page: 1 }));
  };

  const handleSelectPriceDuration = (priceDurationRange: string) => {
    setFilters((prev) => ({ ...prev, priceDurationRange, page: 1 }));
  };

  const handleResetFilters = () => {
    setFilters({
      query: '',
      category: 'all',
      department: 'all',
      priceDurationRange: 'all',
      page: 1,
      pageSize: 6,
    });
  };

  const handlePageChange = (newPage: number) => {
    setFilters((prev) => ({ ...prev, page: newPage }));
    window.scrollTo({ top: 180, behavior: 'smooth' });
  };

  const handleViewDetail = (service: ExaminationService) => {
    setSelectedService(service);
  };

  const handleCloseModal = () => {
    setSelectedService(null);
    if (serviceId || searchParams.has('serviceId')) {
      navigate('/services', { replace: true });
    }
  };

  const handleSelectService = (service: ExaminationService) => {
    selectService(service.id, service.name);
    // Điều hướng sang màn chọn ngày và ca khám theo dịch vụ (không gộp với màn chọn bác sĩ)
    navigate(`/appointment/services/${service.id}/booking`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-[#F8FAFC] font-sans antialiased text-slate-900">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav
            aria-label="Breadcrumb"
            className="mb-4 flex items-center gap-2 text-xs text-slate-500"
          >
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-sky-600">
              <Home className="h-3.5 w-3.5" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="text-slate-500">Đặt khám</span>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="font-semibold text-sky-600">Danh mục Dịch vụ Khám (SCR-APT-03)</span>
          </nav>

          {/* Heading */}
          <div className="mb-6">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
              Danh mục Dịch vụ
            </h1>
          </div>

          {/* Search & Filter Card */}
          <section
            aria-label="Bộ lọc tìm kiếm dịch vụ khám"
            className="mb-7 rounded-2xl border border-slate-200/90 bg-white p-5 shadow-sm space-y-3"
          >
            <ServiceSearchBar initialValue={filters.query} onSearch={handleSearch} />

            <ServiceCategoryTabs
              selectedCategory={filters.category ?? 'all'}
              onSelectCategory={handleSelectCategory}
            />

            <ServiceFilterBar
              selectedDepartment={filters.department ?? 'all'}
              selectedPriceDuration={filters.priceDurationRange ?? 'all'}
              onSelectDepartment={handleSelectDepartment}
              onSelectPriceDuration={handleSelectPriceDuration}
              onResetFilters={handleResetFilters}
              hasActiveFilters={hasActiveFilters}
            />
          </section>

          {/* Service Cards Grid */}
          <section aria-label="Danh sách dịch vụ khám">
            <ServiceGrid
              isLoading={isLoading}
              services={services}
              onViewDetail={handleViewDetail}
              onSelectService={handleSelectService}
              onResetFilters={handleResetFilters}
            />
          </section>

          {/* Pagination */}
          {!isLoading && services.length > 0 && (
            <div className="mt-8">
              <ServicePagination
                currentCount={services.length}
                totalItems={total}
                currentPage={page}
                totalPages={totalPages}
                onPageChange={handlePageChange}
              />
            </div>
          )}
        </div>
      </main>

      {/* Service Detail Modal */}
      <ServiceDetailModal
        service={activeService}
        isOpen={Boolean(activeService)}
        onClose={handleCloseModal}
        onSelectService={handleSelectService}
      />

      <PatientFooter />
    </div>
  );
}
