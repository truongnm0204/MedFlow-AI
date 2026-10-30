import { useEffect, useMemo, useRef } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { AdminServicesShell } from '../components/admin-services-shell';
import { ServiceColumnModal } from '../components/service-column-modal';
import { ServiceFilters } from '../components/service-filters';
import { ServiceMetrics } from '../components/service-metrics';
import { ServicePagination } from '../components/service-pagination';
import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServiceTable } from '../components/service-table';
import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ExaminationService } from '../types/examination-service.types';

function exportServicesToCsv(services: ExaminationService[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = [
    'Mã Dịch Vụ',
    'Tên Dịch Vụ',
    'Mô Tả',
    'Phân Loại',
    'Khoa Phụ Trách',
    'Giá Hiệu Lực (VNĐ)',
    'Đặt Lịch Online',
    'Trạng Thái',
  ];
  const rows = services.map((s) => [
    s.code,
    s.name,
    s.description,
    s.category,
    s.department,
    String(s.price),
    s.onlineBooking ? 'Cho phép Online' : 'Chỉ tại quầy',
    s.status === 'ACTIVE' ? 'Hoạt động' : 'Tạm dừng',
  ]);

  const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-muc-dich-vu-kham-medflow.csv';
  link.click();
  URL.revokeObjectURL(url);
}

export function ExaminationServiceListPage() {
  const navigate = useNavigate();
  const searchInputRef = useRef<HTMLInputElement>(null);

  const services = useExaminationServiceStore((state) => state.services);
  const searchQuery = useExaminationServiceStore((state) => state.searchQuery);
  const categoryFilter = useExaminationServiceStore((state) => state.categoryFilter);
  const departmentFilter = useExaminationServiceStore((state) => state.departmentFilter);
  const bookingFilter = useExaminationServiceStore((state) => state.bookingFilter);
  const statusFilter = useExaminationServiceStore((state) => state.statusFilter);
  const page = useExaminationServiceStore((state) => state.page);
  const pageSize = useExaminationServiceStore((state) => state.pageSize);
  const setPage = useExaminationServiceStore((state) => state.setPage);
  const setPageSize = useExaminationServiceStore((state) => state.setPageSize);
  const setPriceHistoryService = useExaminationServiceStore(
    (state) => state.setPriceHistoryService,
  );

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }
      if (e.key === 'F2') {
        e.preventDefault();
        navigate('/admin/services/new');
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  const filteredServices = useMemo(() => {
    return services.filter((service) => {
      const term = searchQuery.trim().toLowerCase();
      const matchesSearch =
        !term ||
        service.code.toLowerCase().includes(term) ||
        service.name.toLowerCase().includes(term) ||
        service.department.toLowerCase().includes(term) ||
        service.description.toLowerCase().includes(term);

      const matchesCategory = !categoryFilter || service.category === categoryFilter;
      const matchesDepartment = !departmentFilter || service.department === departmentFilter;
      const matchesBooking =
        !bookingFilter ||
        (bookingFilter === 'online' && service.onlineBooking) ||
        (bookingFilter === 'counter' && !service.onlineBooking);
      const matchesStatus = !statusFilter || service.status === statusFilter;

      return (
        matchesSearch && matchesCategory && matchesDepartment && matchesBooking && matchesStatus
      );
    });
  }, [services, searchQuery, categoryFilter, departmentFilter, bookingFilter, statusFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredServices.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paginatedServices = filteredServices.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize,
  );

  return (
    <AdminServicesShell>
      <main className="space-y-5 px-6 py-6">
        <ServiceMetrics services={services} />

        <ServiceFilters
          searchRef={searchInputRef}
          onExportCsv={exportServicesToCsv}
          filteredServices={filteredServices}
        />

        <section
          className="overflow-hidden rounded-xl border border-border bg-white shadow-sm"
          aria-label="Bảng danh mục dịch vụ khám ngoại trú"
        >
          <ServiceTable
            services={paginatedServices}
            onViewHistory={(service) => setPriceHistoryService(service)}
          />

          <ServicePagination
            count={filteredServices.length}
            currentPage={currentPage}
            pageSize={pageSize}
            totalPages={totalPages}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
          />
        </section>
      </main>

      <ServicePriceHistoryModal />
      <ServiceColumnModal />
      <Outlet />
    </AdminServicesShell>
  );
}
