import {
  Calendar,
  CheckCircle2,
  DollarSign,
  FileSpreadsheet,
  History,
  RotateCcw,
  Search,
  Tag,
  TrendingUp,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasAnyRole } from '@/features/auth/utils/rbac';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Button } from '@/shared/ui/button';

import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServicePriceUpdateModal } from '../components/service-price-update-modal';
import { CATEGORIES, DEPARTMENTS } from '../data/examination-services-data';
import { useExaminationServiceStore } from '../store/examination-service.store';

export function ServicePricingPage() {
  const user = useAuthStore((state) => state.user);
  const canUpdatePrice = hasAnyRole(user?.roles, ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN']);

  const services = useExaminationServiceStore((state) => state.services);
  const setPriceHistoryService = useExaminationServiceStore(
    (state) => state.setPriceHistoryService,
  );
  const setUpdatePriceService = useExaminationServiceStore((state) => state.setUpdatePriceService);

  const [searchQuery, setSearchQuery] = useState('');
  const [departmentFilter, setDepartmentFilter] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [priceRangeFilter, setPriceRangeFilter] = useState<
    'all' | 'under300' | '300to500' | 'above500'
  >('all');
  const [statusFilter, setStatusFilter] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const pageSize = 10;

  const formatVnd = (num: number) => num.toLocaleString('vi-VN') + ' đ';

  // KPI Metrics Calculation
  const metrics = useMemo(() => {
    if (services.length === 0) {
      return {
        total: 0,
        avgPrice: 0,
        maxPrice: 0,
        minPrice: 0,
        latestDecision: 'N/A',
        latestDate: 'N/A',
      };
    }
    const prices = services.map((s) => s.price);
    const sum = prices.reduce((acc, p) => acc + p, 0);
    const avg = Math.round(sum / prices.length);
    const max = Math.max(...prices);
    const min = Math.min(...prices);

    // Find the latest decision entry
    const latestService = services.find((s) => s.decisionNumber);
    const latestDecision = latestService?.decisionNumber ?? 'QĐ-142/BV-KH';
    const latestDate = latestService?.effectiveDate ?? '2026-01-01';

    return {
      total: services.length,
      avgPrice: avg,
      maxPrice: max,
      minPrice: min,
      latestDecision,
      latestDate,
    };
  }, [services]);

  // Filtered Services
  const filteredServices = useMemo(() => {
    return services.filter((item) => {
      const q = searchQuery.trim().toLowerCase();
      const matchesQuery =
        !q ||
        item.code.toLowerCase().includes(q) ||
        item.name.toLowerCase().includes(q) ||
        item.department.toLowerCase().includes(q);

      const matchesDept = !departmentFilter || item.department === departmentFilter;
      const matchesCat = !categoryFilter || item.category === categoryFilter;
      const matchesStatus = !statusFilter || item.status === statusFilter;

      let matchesPrice = true;
      if (priceRangeFilter === 'under300') {
        matchesPrice = item.price < 300000;
      } else if (priceRangeFilter === '300to500') {
        matchesPrice = item.price >= 300000 && item.price <= 500000;
      } else if (priceRangeFilter === 'above500') {
        matchesPrice = item.price > 500000;
      }

      return matchesQuery && matchesDept && matchesCat && matchesStatus && matchesPrice;
    });
  }, [services, searchQuery, departmentFilter, categoryFilter, statusFilter, priceRangeFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredServices.length / pageSize));
  const page = Math.min(currentPage, totalPages);
  const paginatedServices = filteredServices.slice((page - 1) * pageSize, page * pageSize);

  const handleResetFilters = () => {
    setSearchQuery('');
    setDepartmentFilter('');
    setCategoryFilter('');
    setPriceRangeFilter('all');
    setStatusFilter('');
    setCurrentPage(1);
  };

  const handleExportCsv = () => {
    const quote = (val: string) => `"${val.replaceAll('"', '""')}"`;
    const headers = [
      'Mã Dịch Vụ',
      'Tên Dịch Vụ Khám',
      'Khoa Quản Lý',
      'Phân Loại',
      'Đơn Giá (VNĐ)',
      'Số Quyết Định',
      'Ngày Hiệu Lực',
      'Trạng Thái',
    ];
    const rows = filteredServices.map((s) => [
      s.code,
      s.name,
      s.department,
      s.category,
      s.price.toString(),
      s.decisionNumber ?? 'QĐ-142/BV-KH',
      s.effectiveDate ?? '2026-01-01',
      s.status === 'ACTIVE' ? 'Hoạt động' : 'Tạm dừng',
    ]);
    const csvContent = [
      headers.map(quote).join(','),
      ...rows.map((r) => r.map(quote).join(',')),
    ].join('\r\n');
    const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `medflow_bang_gia_dich_vu_${new Date().toISOString().split('T')[0]}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <AdminPortalShell
      activeTab="Bảng giá & Biểu phí"
      breadcrumbs={[{ label: 'Cấu hình Cơ sở' }, { label: 'Bảng giá & Biểu phí Dịch vụ (UC52)' }]}
    >
      <main className="space-y-6 px-6 py-6 font-sans">
        {/* Header Title Section */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="flex items-center gap-2">
              <span className="rounded-md bg-emerald-100 px-2 py-0.5 text-[11px] font-bold text-emerald-800">
                UC52 • Quản lý Biểu phí Cơ sở
              </span>
              <span className="rounded-md bg-sky-100 px-2 py-0.5 text-[11px] font-bold text-primary">
                Hospital Admin
              </span>
            </div>
            <h1 className="mt-1.5 text-xl font-bold text-ink sm:text-2xl">
              Bảng giá & Biểu phí Dịch vụ Khám Ngoại trú
            </h1>
            <p className="mt-0.5 text-xs text-sub">
              Niêm yết biểu phí dịch vụ khám ngoại trú, quản lý lịch sử quyết định giá và điều chỉnh
              giá theo chính sách bệnh viện.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" onClick={handleExportCsv} className="gap-1.5">
              <FileSpreadsheet className="h-4 w-4 text-emerald-600" aria-hidden="true" />
              <span>Xuất Biểu phí CSV</span>
            </Button>
            <Link
              to="/admin/services"
              className="inline-flex items-center justify-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 transition-colors"
            >
              <span>Xem Danh mục Kỹ thuật →</span>
            </Link>
          </div>
        </div>

        {/* 4 KPI Metric Cards */}
        <section
          aria-label="Chỉ số biểu phí dịch vụ"
          className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4"
        >
          {/* Card 1 */}
          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                TỔNG DỊCH VỤ ĐỊNH GIÁ
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-100 text-primary">
                <Tag className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-ink">{metrics.total}</div>
            <div className="mt-1 flex items-center gap-1 text-[11px] text-emerald-700 font-medium">
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span>100% dịch vụ đã áp dụng giá</span>
            </div>
          </div>

          {/* Card 2 */}
          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                ĐƠN GIÁ TRUNG BÌNH
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-100 text-emerald-700">
                <DollarSign className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-ink">{formatVnd(metrics.avgPrice)}</div>
            <div className="mt-1 text-[11px] text-sub">Biểu phí trung bình toàn viện</div>
          </div>

          {/* Card 3 */}
          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                KHOẢNG GIÁ NIÊM YẾT
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-100 text-amber-700">
                <TrendingUp className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-base font-bold text-ink truncate">
              {formatVnd(metrics.minPrice)} - {formatVnd(metrics.maxPrice)}
            </div>
            <div className="mt-1 text-[11px] text-sub">Tối thiểu đến tối đa</div>
          </div>

          {/* Card 4 */}
          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                QUYẾT ĐỊNH GẦN NHẤT
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-100 text-indigo-700">
                <Calendar className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-base font-bold text-ink truncate font-mono">
              {metrics.latestDecision}
            </div>
            <div className="mt-1 text-[11px] text-sub">Hiệu lực: {metrics.latestDate}</div>
          </div>
        </section>

        {/* Filter Bar */}
        <section
          aria-label="Bộ lọc biểu phí"
          className="rounded-xl border border-border bg-white p-4 shadow-xs space-y-3"
        >
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {/* Search Input */}
            <div className="relative lg:col-span-2">
              <Search
                className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
                aria-hidden="true"
              />
              <input
                id="pricing-search-input"
                type="text"
                value={searchQuery}
                onChange={(e) => {
                  setSearchQuery(e.target.value);
                  setCurrentPage(1);
                }}
                placeholder="Tìm mã hoặc tên dịch vụ khám..."
                className="w-full rounded-lg border border-border bg-slate-50 py-2 pl-9 pr-3 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none"
              />
            </div>

            {/* Department Select */}
            <div>
              <select
                id="pricing-department-filter"
                value={departmentFilter}
                onChange={(e) => {
                  setDepartmentFilter(e.target.value);
                  setCurrentPage(1);
                }}
                className="w-full rounded-lg border border-border bg-slate-50 px-3 py-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
              >
                <option value="">Tất cả khoa phòng</option>
                {DEPARTMENTS.map((dept) => (
                  <option key={dept} value={dept}>
                    {dept}
                  </option>
                ))}
              </select>
            </div>

            {/* Category Select */}
            <div>
              <select
                id="pricing-category-filter"
                value={categoryFilter}
                onChange={(e) => {
                  setCategoryFilter(e.target.value);
                  setCurrentPage(1);
                }}
                className="w-full rounded-lg border border-border bg-slate-50 px-3 py-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
              >
                <option value="">Tất cả phân loại</option>
                {CATEGORIES.map((cat) => (
                  <option key={cat} value={cat}>
                    {cat}
                  </option>
                ))}
              </select>
            </div>

            {/* Price Range Select */}
            <div>
              <select
                id="pricing-range-filter"
                value={priceRangeFilter}
                onChange={(e) => {
                  setPriceRangeFilter(e.target.value as typeof priceRangeFilter);
                  setCurrentPage(1);
                }}
                className="w-full rounded-lg border border-border bg-slate-50 px-3 py-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
              >
                <option value="all">Tất cả mức giá</option>
                <option value="under300">Dưới 300.000 đ</option>
                <option value="300to500">300.000 đ - 500.000 đ</option>
                <option value="above500">Trên 500.000 đ</option>
              </select>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-slate-100 text-xs">
            <div className="text-sub">
              Hiển thị <strong>{filteredServices.length}</strong> /{' '}
              <strong>{services.length}</strong> dịch vụ
            </div>
            <button
              type="button"
              onClick={handleResetFilters}
              className="inline-flex items-center gap-1 text-[11px] font-medium text-slate-500 hover:text-primary transition-colors cursor-pointer"
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đặt lại bộ lọc</span>
            </button>
          </div>
        </section>

        {/* Pricing Table Section */}
        <section
          aria-label="Danh sách biểu phí dịch vụ"
          className="overflow-hidden rounded-xl border border-border bg-white shadow-xs"
        >
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1100px] table-fixed text-left text-xs">
              <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wider text-sub">
                <tr>
                  <th className="w-[9%] px-4 py-3.5">MÃ DỊCH VỤ</th>
                  <th className="w-[26%] px-4 py-3.5">TÊN DỊCH VỤ KHÁM NGOẠI TRÚ</th>
                  <th className="w-[15%] px-4 py-3.5">KHOA QUẢN LÝ</th>
                  <th className="w-[12%] px-4 py-3.5">PHÂN LOẠI</th>
                  <th className="w-[11%] px-4 py-3.5">GIÁ HIỆN HÀNH</th>
                  <th className="w-[11%] px-4 py-3.5">QUYẾT ĐỊNH</th>
                  <th className="w-[16%] px-4 py-3.5 text-right pr-6">THAO TÁC</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {paginatedServices.map((service) => (
                  <tr key={service.id} className="hover:bg-sky-50/30 transition-colors">
                    {/* Code */}
                    <td className="px-4 py-3.5 font-mono text-xs font-bold text-slate-800">
                      {service.code}
                    </td>

                    {/* Name */}
                    <td className="px-4 py-3.5">
                      <div className="font-bold text-ink leading-snug">{service.name}</div>
                      <div className="text-[11px] text-slate-500 mt-0.5 line-clamp-1">
                        {service.description}
                      </div>
                    </td>

                    {/* Department */}
                    <td className="px-4 py-3.5 text-slate-700 font-medium">{service.department}</td>

                    {/* Category */}
                    <td className="px-4 py-3.5">
                      <span className="inline-block rounded-full bg-slate-100 px-2.5 py-0.5 text-[10px] font-semibold text-slate-700">
                        {service.category}
                      </span>
                    </td>

                    {/* Price */}
                    <td className="px-4 py-3.5">
                      <span className="font-bold text-sm text-emerald-800">
                        {formatVnd(service.price)}
                      </span>
                    </td>

                    {/* Decision */}
                    <td className="px-4 py-3.5">
                      <div className="font-mono text-[11px] font-semibold text-slate-700">
                        {service.decisionNumber ?? 'QĐ-142/BV-KH'}
                      </div>
                      <div className="text-[10px] text-slate-400 mt-0.5">
                        {service.effectiveDate ?? '01/01/2026'}
                      </div>
                    </td>

                    {/* Actions */}
                    <td className="px-4 py-3.5 text-right pr-6">
                      <div className="flex items-center justify-end gap-1.5 whitespace-nowrap">
                        {canUpdatePrice && (
                          <Button
                            size="sm"
                            onClick={() => setUpdatePriceService(service)}
                            className="h-7 px-2.5 text-[11px] gap-1 whitespace-nowrap"
                            title={`Cập nhật giá cho ${service.name}`}
                            aria-label={`Cập nhật giá ${service.name}`}
                          >
                            <DollarSign className="h-3 w-3" aria-hidden="true" />
                            <span>Cập nhật giá</span>
                          </Button>
                        )}
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => setPriceHistoryService(service)}
                          className="h-7 px-2 text-[11px] gap-1 text-sub hover:text-ink"
                          title={`Xem lịch sử giá của ${service.name}`}
                          aria-label={`Xem lịch sử giá ${service.name}`}
                        >
                          <History className="h-3 w-3" aria-hidden="true" />
                          <span className="hidden sm:inline">Lịch sử</span>
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {filteredServices.length === 0 && (
            <div className="py-16 text-center text-sub">
              <p className="text-sm font-semibold text-slate-700">
                Không tìm thấy biểu phí dịch vụ phù hợp với bộ lọc.
              </p>
              <p className="mt-1 text-xs text-slate-400">
                Hãy thử kiểm tra lại từ khóa tìm kiếm hoặc bấm đặt lại bộ lọc.
              </p>
              <Button variant="secondary" size="sm" onClick={handleResetFilters} className="mt-4">
                Đặt lại bộ lọc
              </Button>
            </div>
          )}

          {/* Pagination */}
          {filteredServices.length > pageSize && (
            <div className="flex items-center justify-between border-t border-border px-6 py-3 bg-slate-50/50 text-xs">
              <span className="text-sub">
                Trang <strong>{page}</strong> / <strong>{totalPages}</strong>
              </span>
              <div className="flex items-center gap-1.5">
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                  className="h-7 px-2.5 text-xs"
                >
                  Trước
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                  className="h-7 px-2.5 text-xs"
                >
                  Sau
                </Button>
              </div>
            </div>
          )}
        </section>
      </main>

      {/* Pricing Modals */}
      <ServicePriceUpdateModal />
      <ServicePriceHistoryModal />
    </AdminPortalShell>
  );
}
