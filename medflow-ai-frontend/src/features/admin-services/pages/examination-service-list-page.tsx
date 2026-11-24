import { CheckCircle2, Loader2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { AdminServicesShell } from '../components/admin-services-shell';
import { ServiceColumnModal } from '../components/service-column-modal';
import { ServiceDeactivateModal } from '../components/service-deactivate-modal';
import { ServiceFilters } from '../components/service-filters';
import { ServiceMetrics } from '../components/service-metrics';
import { ServicePagination } from '../components/service-pagination';
import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServicePriceUpdateModal } from '../components/service-price-update-modal';
import { ServiceTable } from '../components/service-table';
import {
  useAdminExaminationServices,
  useDeactivateExaminationServiceMutation,
  useReactivateExaminationServiceMutation,
} from '../hooks/use-examination-services';
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

  const { isLoading, isError, error, refetch } = useAdminExaminationServices();
  const deactivateMutation = useDeactivateExaminationServiceMutation();
  const reactivateMutation = useReactivateExaminationServiceMutation();

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
  const updateService = useExaminationServiceStore((state) => state.updateService);

  const [deactivatingService, setDeactivatingService] = useState<ExaminationService | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const handleConfirmDeactivate = async (serviceId: string) => {
    const service = services.find((item) => item.id === serviceId);
    if (!service) {
      setDeactivatingService(null);
      return;
    }
    setActionError(null);
    try {
      await deactivateMutation.mutateAsync({ idOrCode: service.code || service.id });
      updateService(serviceId, { status: 'SUSPENDED' });
      setFeedbackMessage(`Đã chuyển trạng thái dịch vụ ${service.name} sang Tạm ngừng hoạt động.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể tạm ngừng dịch vụ khám.');
    } finally {
      setDeactivatingService(null);
    }
  };

  const handleToggleStatus = async (id: string) => {
    const service = services.find((item) => item.id === id);
    if (!service) return;
    if (service.status === 'ACTIVE') {
      setDeactivatingService(service);
      return;
    }

    setActionError(null);
    try {
      await reactivateMutation.mutateAsync(service.code || service.id);
      updateService(id, { status: 'ACTIVE' });
      setFeedbackMessage(`Đã kích hoạt lại hoạt động cho dịch vụ ${service.name}.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể kích hoạt lại dịch vụ khám.');
    }
  };

  const handleBatchToggleStatus = async (ids: string[], newStatus: 'ACTIVE' | 'SUSPENDED') => {
    setActionError(null);
    try {
      await Promise.all(
        ids.map(async (id) => {
          const item = services.find((s) => s.id === id);
          if (!item) return;
          if (newStatus === 'SUSPENDED') {
            await deactivateMutation.mutateAsync({ idOrCode: item.code || item.id });
          } else {
            await reactivateMutation.mutateAsync(item.code || item.id);
          }
          updateService(id, { status: newStatus });
        }),
      );
      setFeedbackMessage(
        newStatus === 'SUSPENDED'
          ? `Đã tạm ngừng ${ids.length} dịch vụ khám được chọn.`
          : `Đã kích hoạt lại ${ids.length} dịch vụ khám được chọn.`,
      );
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(
        err instanceof Error ? err.message : 'Không thể cập nhật trạng thái hàng loạt.',
      );
    }
  };

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
    <AdminServicesShell variant="examination">
      <main className="space-y-5 px-6 py-6">
        {isError ? (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                <b>Không thể tải danh mục dịch vụ khám:</b>{' '}
                {error instanceof Error ? error.message : 'Đã xảy ra lỗi kết nối máy chủ.'}
              </span>
              <Button type="button" size="sm" variant="secondary" onClick={() => void refetch()}>
                Thử lại
              </Button>
            </div>
          </Alert>
        ) : null}

        {actionError ? <Alert tone="error">{actionError}</Alert> : null}

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
          {isLoading && services.length === 0 ? (
            <div className="flex items-center justify-center gap-2 py-12 text-sm text-sub">
              <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
              <span>Đang tải danh mục dịch vụ khám...</span>
            </div>
          ) : (
            <>
              <ServiceTable
                services={paginatedServices}
                onViewHistory={(service) => setPriceHistoryService(service)}
                onToggleStatus={(id) => void handleToggleStatus(id)}
                onRequestDeactivate={(service) => setDeactivatingService(service)}
                onBatchToggleStatus={(ids, nextStatus) =>
                  void handleBatchToggleStatus(ids, nextStatus)
                }
              />
              <ServicePagination
                count={filteredServices.length}
                currentPage={currentPage}
                pageSize={pageSize}
                totalPages={totalPages}
                onPageChange={setPage}
                onPageSizeChange={setPageSize}
              />
            </>
          )}
        </section>
      </main>

      <ServicePriceHistoryModal />
      <ServicePriceUpdateModal />
      <ServiceColumnModal />
      <ServiceDeactivateModal
        service={deactivatingService}
        open={Boolean(deactivatingService)}
        onClose={() => setDeactivatingService(null)}
        onConfirm={(serviceId) => void handleConfirmDeactivate(serviceId)}
      />
      {feedbackMessage && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-semibold text-emerald-800 shadow-lg animate-in fade-in"
        >
          <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
          <span>{feedbackMessage}</span>
        </div>
      )}
      <Outlet />
    </AdminServicesShell>
  );
}
