import { AlertTriangle, CheckCircle, RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { PricingFilters } from '../components/pricing-filters';
import { PricingHistoryModal } from '../components/pricing-history-modal';
import { PricingMetrics } from '../components/pricing-metrics';
import { PricingPolicyDrawer } from '../components/pricing-policy-drawer';
import { PricingTable } from '../components/pricing-table';
import { PricingUpdateModal } from '../components/pricing-update-modal';
import { calculateMetrics, INITIAL_PRICING_DATA } from '../data/service-pricing-mock';
import { useServicePricing, useUpdateServicePrice } from '../hooks/use-service-pricing';
import type {
  ServicePricingFilter,
  ServicePricingItem,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

const INITIAL_FILTERS: ServicePricingFilter = {
  search: '',
  department: 'Tất cả khoa phòng',
  category: 'all',
  priceRange: 'all',
  status: 'all',
};

function exportPricingToCsv(items: ServicePricingItem[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = [
    'Mã Dịch Vụ',
    'Tên Dịch Vụ',
    'Mô Tả',
    'Khoa Phụ Trách',
    'Phân Loại',
    'Đơn Giá (VNĐ)',
    'Số Quyết Định',
    'Ngày Hiệu Lực',
    'Trạng Thái',
    'Đặt Lịch Online',
  ];

  const rows = items.map((item) => [
    item.code,
    item.name,
    item.description,
    item.department,
    item.category,
    String(item.currentPrice),
    item.decisionNumber,
    item.effectiveDate,
    item.status === 'ACTIVE' ? 'Đang áp dụng' : 'Tạm dừng',
    item.onlineBooking ? 'Cho phép online' : 'Chỉ tại quầy',
  ]);

  const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `bang-gia-dich-vu-medflow-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

export function ServicePricingPage() {
  const searchInputRef = useRef<HTMLInputElement>(null);

  const [filters, setFilters] = useState<ServicePricingFilter>(INITIAL_FILTERS);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedHistoryItem, setSelectedHistoryItem] = useState<ServicePricingItem | null>(null);
  const [selectedUpdateItem, setSelectedUpdateItem] = useState<ServicePricingItem | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const updateMutation = useUpdateServicePrice();
  const [isPolicyDrawerOpen, setIsPolicyDrawerOpen] = useState(false);

  const { data, isLoading, isError, error, refetch } = useServicePricing(filters, page, pageSize);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const handleFilterChange = (partial: Partial<ServicePricingFilter>) => {
    setFilters((prev) => ({ ...prev, ...partial }));
    setPage(1);
  };

  const handleResetFilters = () => {
    setFilters(INITIAL_FILTERS);
    setPage(1);
  };

  const handleExportCsv = () => {
    const itemsToExport = data?.data && data.data.length > 0 ? data.data : INITIAL_PRICING_DATA;
    exportPricingToCsv(itemsToExport);
  };

  const handlePrint = () => {
    window.print();
  };
  const handleUpdatePrice = async (id: string, payload: UpdateServicePricePayload) => {
    await updateMutation.mutateAsync({ id, payload });
    setSuccessMessage(
      `Đã cập nhật thành công mức giá mới ${payload.newPrice.toLocaleString('vi-VN')} đ theo quyết định ${payload.decisionNumber}.`,
    );
    setTimeout(() => setSuccessMessage(null), 5000);
  };

  const metrics = data?.metrics || calculateMetrics(INITIAL_PRICING_DATA);
  const items = data?.data || [];
  const total = data?.total || 0;
  const totalPages = data?.totalPages || 1;

  return (
    <AdminPortalShell
      activeTab="Bảng giá & Biểu phí"
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Bảng giá & Biểu phí Dịch vụ (SCR-ADM-PRC-01)' },
      ]}
    >
      <div className="mx-auto max-w-[1720px] space-y-6 p-6">
        {/* Page Title & Context Header */}
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-xl font-bold tracking-tight text-ink">
                Bảng Giá & Biểu Phí Dịch Vụ Y Tế
              </h1>
              <span className="rounded bg-sky-100 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">
                UC52
              </span>
            </div>
            <p className="mt-1 text-xs text-sub">
              Niêm yết công khai và quản trị biểu phí áp dụng cho dịch vụ khám ngoại trú, hội chẩn
              và cận lâm sàng theo quy định Bộ Y Tế.
            </p>
          </div>
        </div>

        {/* 4 Stat Cards */}
        <PricingMetrics metrics={metrics} />

        {/* Filter & Action Toolbar */}
        <PricingFilters
          filters={filters}
          onChange={handleFilterChange}
          onReset={handleResetFilters}
          onExportCsv={handleExportCsv}
          onPrint={handlePrint}
          onOpenPolicy={() => setIsPolicyDrawerOpen(true)}
          searchInputRef={searchInputRef}
          totalCount={total}
        />

        {/* Success Alert */}
        {successMessage && (
          <div className="flex items-center justify-between rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-xs text-emerald-900 shadow-xs animate-in fade-in duration-150">
            <div className="flex items-center gap-2">
              <CheckCircle className="h-4 w-4 text-emerald-600 shrink-0" aria-hidden="true" />
              <span className="font-semibold">{successMessage}</span>
            </div>
            <button
              type="button"
              onClick={() => setSuccessMessage(null)}
              className="text-emerald-700 hover:text-emerald-900 font-bold px-2 py-0.5 rounded text-xs"
            >
              Đóng
            </button>
          </div>
        )}

        {/* Error Alert with Retry */}
        {isError && (
          <div className="flex items-center justify-between rounded-xl border border-rose-200 bg-rose-50 p-4 text-xs text-rose-900 shadow-xs">
            <div className="flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-rose-600 shrink-0" aria-hidden="true" />
              <span>
                Không thể tải dữ liệu biểu phí từ máy chủ ({error?.message || 'Lỗi kết nối'}). Đang
                hiển thị dữ liệu ngoại tuyến.
              </span>
            </div>
            <button
              type="button"
              onClick={() => refetch()}
              className="inline-flex items-center gap-1 rounded-md border border-rose-300 bg-white px-2.5 py-1 text-xs font-semibold text-rose-700 shadow-xs hover:bg-rose-50 transition-colors"
            >
              <RefreshCw className="h-3 w-3" />
              <span>Thử lại</span>
            </button>
          </div>
        )}

        {/* Table View */}
        <PricingTable
          items={items}
          isLoading={isLoading}
          page={page}
          pageSize={pageSize}
          totalPages={totalPages}
          total={total}
          onPageChange={(newPage) => setPage(newPage)}
          onPageSizeChange={(newPageSize) => {
            setPageSize(newPageSize);
            setPage(1);
          }}
          onViewHistory={(item) => setSelectedHistoryItem(item)}
          onUpdatePrice={(item) => setSelectedUpdateItem(item)}
          onResetFilters={handleResetFilters}
        />
      </div>

      {/* Price History Modal */}
      <PricingHistoryModal
        item={selectedHistoryItem}
        onClose={() => setSelectedHistoryItem(null)}
      />
      {/* Price Update Modal */}
      <PricingUpdateModal
        item={selectedUpdateItem}
        isOpen={Boolean(selectedUpdateItem)}
        onClose={() => setSelectedUpdateItem(null)}
        onSubmit={handleUpdatePrice}
        isSubmitting={updateMutation.isPending}
      />

      {/* UC52 Legal Policy Drawer */}
      <PricingPolicyDrawer
        isOpen={isPolicyDrawerOpen}
        onClose={() => setIsPolicyDrawerOpen(false)}
      />
    </AdminPortalShell>
  );
}
