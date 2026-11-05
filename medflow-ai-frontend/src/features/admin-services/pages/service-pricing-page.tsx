import {
  CheckCircle,
  Clock,
  Coins,
  CreditCard,
  FileSpreadsheet,
  History,
  PauseCircle,
  Pencil,
  Printer,
  RotateCcw,
  Search,
  X,
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { ServicePagination } from '../components/service-pagination';
import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServicePriceUpdateModal } from '../components/service-price-update-modal';
import { DEPARTMENTS } from '../data/examination-services-data';
import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ExaminationService } from '../types/examination-service.types';

function exportPricingToCsv(services: ExaminationService[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = ['Mã DV', 'Tên Dịch Vụ', 'Khoa Phụ Trách', 'Phân Loại', 'Đơn Giá (VNĐ)', 'Số Quyết Định', 'Ngày Hiệu Lực', 'Trạng Thái'];
  const rows = services.map((s) => [
    s.code,
    s.name,
    s.department,
    s.category,
    String(s.price),
    s.decisionNumber || 'QĐ-142/BV-KH',
    s.effectiveDate || '2026-01-01',
    s.status === 'ACTIVE' ? 'Đang áp dụng' : 'Tạm dừng',
  ]);
  const csv = '\uFEFF' + [headers, ...rows].map((r) => r.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `bang-gia-dich-vu-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

export function ServicePricingPage() {
  const searchInputRef = useRef<HTMLInputElement>(null);

  const services = useExaminationServiceStore((state) => state.services);
  const setPriceHistoryService = useExaminationServiceStore((state) => state.setPriceHistoryService);

  const [search, setSearch] = useState('');
  const [department, setDepartment] = useState('all');
  const [status, setStatus] = useState('all');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selectedUpdateService, setSelectedUpdateService] = useState<ExaminationService | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

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

  const filteredServices = useMemo(() => {
    return services.filter((s) => {
      if (search) {
        const q = search.trim().toLowerCase();
        const match =
          s.code.toLowerCase().includes(q) ||
          s.name.toLowerCase().includes(q) ||
          s.description.toLowerCase().includes(q);
        if (!match) return false;
      }
      if (department !== 'all' && s.department !== department) return false;
      if (status !== 'all' && s.status !== status) return false;
      return true;
    });
  }, [services, search, department, status]);

  const totalPages = Math.max(1, Math.ceil(filteredServices.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paginatedServices = filteredServices.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  // Metrics calculation
  const total = services.length;
  const activeCount = services.filter((s) => s.status === 'ACTIVE').length;
  const suspendedCount = services.filter((s) => s.status === 'SUSPENDED').length;
  const avgPrice = total > 0 ? Math.round(services.reduce((acc, s) => acc + s.price, 0) / total) : 0;

  const handleResetFilters = () => {
    setSearch('');
    setDepartment('all');
    setStatus('all');
    setPage(1);
  };

  const handleUpdateSuccess = (updated: ExaminationService) => {
    setSuccessMessage(
      `Đã cập nhật mức giá mới ${updated.price.toLocaleString('vi-VN')} đ theo ${updated.decisionNumber || 'QĐ mới'} thành công.`,
    );
    setTimeout(() => setSuccessMessage(null), 5000);
  };

  const isFiltered = Boolean(search.trim()) || department !== 'all' || status !== 'all';

  return (
    <AdminPortalShell
      activeTab="Bảng giá & Biểu phí"
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Bảng giá & Biểu phí Dịch vụ (SCR-ADM-PRC-01)' },
      ]}
    >
      <div className="mx-auto max-w-[1720px] space-y-5 p-6">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-bold tracking-tight text-ink">Bảng Giá & Biểu Phí Dịch Vụ Y Tế</h1>
            <span className="rounded bg-sky-100 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">UC52</span>
          </div>
          <p className="mt-1 text-xs text-sub">
            Niêm yết công khai và quản trị biểu phí áp dụng cho dịch vụ khám ngoại trú, hội chẩn và cận lâm sàng.
          </p>
        </div>

        {/* 4 Stat Cards */}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="flex items-center justify-between rounded-xl border border-border bg-white p-4 shadow-xs">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wider text-sub">DỊCH VỤ NIÊM YẾT</p>
              <div className="mt-1 text-2xl font-bold tracking-tight text-ink">{String(total).padStart(2, '0')}</div>
              <p className="mt-1 text-[11px] text-sub">Danh mục dịch vụ y tế & kỹ thuật</p>
            </div>
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
              <CreditCard className="h-5 w-5" aria-hidden="true" />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-xl border border-border bg-white p-4 shadow-xs">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wider text-sub">ĐANG ÁP DỤNG HIỆU LỰC</p>
              <div className="mt-1 text-2xl font-bold tracking-tight text-ink">{String(activeCount).padStart(2, '0')}</div>
              <p className="mt-1 text-[11px] text-emerald-700 font-medium">● Biểu phí hiện hành đã phê duyệt</p>
            </div>
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
              <CheckCircle className="h-5 w-5" aria-hidden="true" />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-xl border border-border bg-white p-4 shadow-xs">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wider text-sub">TẠM DỪNG NIÊM YẾT</p>
              <div className="mt-1 text-2xl font-bold tracking-tight text-ink">{String(suspendedCount).padStart(2, '0')}</div>
              <p className="mt-1 text-[11px] text-amber-700 font-medium">● Cần thẩm định hoặc điều chỉnh</p>
            </div>
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-amber-50 text-amber-600">
              <PauseCircle className="h-5 w-5" aria-hidden="true" />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-xl border border-border bg-white p-4 shadow-xs">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wider text-sub">MỨC GIÁ TRUNG BÌNH</p>
              <div className="mt-1 text-2xl font-bold tracking-tight text-ink">{avgPrice.toLocaleString('vi-VN')} đ</div>
              <p className="mt-1 text-[11px] text-sub">Giá thu bình quân dịch vụ</p>
            </div>
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-indigo-50 text-indigo-600">
              <Coins className="h-5 w-5" aria-hidden="true" />
            </div>
          </div>
        </div>

        {/* Filters Toolbar */}
        <div className="space-y-3 rounded-xl border border-border bg-white p-4 shadow-xs">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden="true" />
              <input
                ref={searchInputRef}
                type="text"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  setPage(1);
                }}
                placeholder="Tìm theo mã DV (EX-*), tên dịch vụ..."
                aria-label="Tìm kiếm dịch vụ và biểu phí"
                className="h-9 w-full rounded-lg border border-border bg-slate-50/50 pl-9 pr-16 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none focus:ring-1 focus:ring-primary"
              />
              {search ? (
                <button
                  type="button"
                  onClick={() => setSearch('')}
                  className="absolute right-3 top-1/2 -translate-y-1/2 rounded p-1 text-slate-400 hover:text-slate-600"
                  aria-label="Xóa nội dung tìm kiếm"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              ) : (
                <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 hidden rounded border border-border bg-white px-1.5 py-0.5 text-[10px] font-mono text-slate-400 sm:inline-block">
                  Ctrl+K
                </kbd>
              )}
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => exportPricingToCsv(filteredServices)}
                className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50"
              >
                <FileSpreadsheet className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                <span>Xuất CSV</span>
              </button>
              <button
                type="button"
                onClick={() => window.print()}
                className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50"
              >
                <Printer className="h-3.5 w-3.5 text-slate-600" aria-hidden="true" />
                <span>In biểu phí</span>
              </button>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 lg:grid-cols-2">
            <select
              aria-label="Khoa phòng phụ trách"
              value={department}
              onChange={(e) => {
                setDepartment(e.target.value);
                setPage(1);
              }}
              className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none"
            >
              <option value="all">Tất cả khoa phòng</option>
              {DEPARTMENTS.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>

            <select
              aria-label="Trạng thái áp dụng"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(1);
              }}
              className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none"
            >
              <option value="all">Tất cả trạng thái</option>
              <option value="ACTIVE">Đang áp dụng</option>
              <option value="SUSPENDED">Tạm dừng niêm yết</option>
            </select>
          </div>

          <div className="flex items-center justify-between border-t border-slate-100 pt-2 text-xs text-sub">
            <span>
              Tìm thấy <strong className="text-ink">{filteredServices.length}</strong> dịch vụ trong danh mục
            </span>
            {isFiltered && (
              <button
                type="button"
                onClick={handleResetFilters}
                className="inline-flex items-center gap-1 font-semibold text-primary hover:text-primary-dark"
              >
                <RotateCcw className="h-3 w-3" aria-hidden="true" />
                <span>Đặt lại bộ lọc</span>
              </button>
            )}
          </div>
        </div>

        {/* Success Alert */}
        {successMessage && (
          <div className="flex items-center justify-between rounded-xl border border-emerald-200 bg-emerald-50 p-3.5 text-xs text-emerald-900 shadow-xs">
            <div className="flex items-center gap-2">
              <CheckCircle className="h-4 w-4 text-emerald-600 shrink-0" aria-hidden="true" />
              <span className="font-semibold">{successMessage}</span>
            </div>
            <button type="button" onClick={() => setSuccessMessage(null)} className="text-emerald-700 font-bold text-xs">
              Đóng
            </button>
          </div>
        )}

        {/* Table View */}
        <div className="rounded-xl border border-border bg-white shadow-xs overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="border-b border-border bg-slate-50 text-[11px] font-bold uppercase text-sub">
                <tr>
                  <th className="px-4 py-3">Mã DV</th>
                  <th className="px-4 py-3">Tên Dịch Vụ</th>
                  <th className="px-4 py-3">Khoa Phụ Trách</th>
                  <th className="px-4 py-3">Phân Loại</th>
                  <th className="px-4 py-3 text-right">Đơn Giá Niêm Yết</th>
                  <th className="px-4 py-3">Số Quyết Định</th>
                  <th className="px-4 py-3 text-center">Trạng Thái</th>
                  <th className="px-4 py-3 text-center">Thao Tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {paginatedServices.map((service) => (
                  <tr key={service.id} className="hover:bg-slate-50/70 transition-colors">
                    <td className="px-4 py-3 whitespace-nowrap">
                      <span className="font-mono text-[11px] font-bold text-primary bg-sky-50 border border-sky-100 px-2 py-0.5 rounded">
                        {service.code}
                      </span>
                    </td>
                    <td className="px-4 py-3 max-w-xs">
                      <div className="font-bold text-ink text-xs">{service.name}</div>
                      <div className="line-clamp-1 text-[11px] text-sub">{service.description}</div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-slate-700">{service.department}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-600 border border-slate-200">
                        {service.category}
                      </span>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-right font-bold text-sm text-emerald-700">
                      {service.price.toLocaleString('vi-VN')} đ
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-slate-600">
                      <div className="font-mono text-[11px] font-semibold">{service.decisionNumber || 'QĐ-142/BV-KH'}</div>
                      <div className="flex items-center gap-1 text-[10px] text-sub">
                        <Clock className="h-3 w-3 text-slate-400" />
                        <span>Từ: {service.effectiveDate || '2026-01-01'}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-center">
                      {service.status === 'ACTIVE' ? (
                        <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-[10px] font-bold text-emerald-700">
                          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" /> Đang áp dụng
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-0.5 text-[10px] font-bold text-amber-700">
                          <span className="h-1.5 w-1.5 rounded-full bg-amber-500" /> Tạm dừng
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-center">
                      <div className="flex items-center justify-center gap-1.5">
                        <button
                          type="button"
                          onClick={() => setSelectedUpdateService(service)}
                          className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50/70 px-2.5 py-1 text-[11px] font-semibold text-emerald-700 hover:bg-emerald-100"
                          aria-label={`Cập nhật giá của ${service.name}`}
                        >
                          <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Cập nhật giá</span>
                        </button>
                        <button
                          type="button"
                          onClick={() => setPriceHistoryService(service)}
                          className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2 py-1 text-[11px] font-semibold text-primary hover:bg-sky-50"
                          aria-label={`Xem lịch sử giá của ${service.name}`}
                        >
                          <History className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Lịch sử giá</span>
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ServicePagination
            count={filteredServices.length}
            currentPage={currentPage}
            pageSize={pageSize}
            totalPages={totalPages}
            onPageChange={setPage}
            onPageSizeChange={(newSize) => {
              setPageSize(newSize);
              setPage(1);
            }}
          />
        </div>
      </div>

      <ServicePriceHistoryModal />
      <ServicePriceUpdateModal
        service={selectedUpdateService}
        isOpen={Boolean(selectedUpdateService)}
        onClose={() => setSelectedUpdateService(null)}
        onSuccess={handleUpdateSuccess}
      />
    </AdminPortalShell>
  );
}
