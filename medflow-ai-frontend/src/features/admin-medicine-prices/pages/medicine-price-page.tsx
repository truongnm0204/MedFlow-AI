import {
  CalendarClock,
  CheckCircle2,
  DollarSign,
  History,
  Pill,
  RotateCcw,
  Search,
  Tags,
  TrendingUp,
} from 'lucide-react';
import { useMemo, useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasAnyRole } from '@/features/auth/utils/rbac';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { MedicinePriceHistoryModal } from '../components/medicine-price-history-modal';
import { MedicinePriceUpdateModal } from '../components/medicine-price-update-modal';
import { useAdminMedicinePrices } from '../hooks/use-medicine-prices';
import { useMedicinePriceStore } from '../store/medicine-price.store';
import type { MedicinePricingMetrics } from '../types/medicine-price.types';

const EMPTY_METRICS: MedicinePricingMetrics = {
  totalMedicines: 0,
  pricedMedicinesCount: 0,
  pricedRatioPercent: 0,
  averagePrice: 0,
  minPrice: 0,
  maxPrice: 0,
  latestEffectiveDate: null,
};

function formatVnd(amount: number): string {
  return `${amount.toLocaleString('vi-VN')} đ`;
}

export function MedicinePricePage() {
  const user = useAuthStore((state) => state.user);
  const canUpdatePrice = hasAnyRole(user?.roles, ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN']);

  const pricingQuery = useAdminMedicinePrices();
  const setUpdatePriceMedicine = useMedicinePriceStore((state) => state.setUpdatePriceMedicine);
  const setPriceHistoryMedicine = useMedicinePriceStore((state) => state.setPriceHistoryMedicine);

  const [searchQuery, setSearchQuery] = useState('');
  const [priceStatusFilter, setPriceStatusFilter] = useState<'' | 'priced' | 'unpriced'>('');
  const [activeFilter, setActiveFilter] = useState<'' | 'active' | 'inactive'>('');
  const [currentPage, setCurrentPage] = useState(1);
  const pageSize = 10;

  const medicines = pricingQuery.data?.data ?? [];
  const metrics = pricingQuery.data?.metrics ?? EMPTY_METRICS;

  const filteredMedicines = useMemo(() => {
    const medicines = pricingQuery.data?.data ?? [];
    return medicines.filter((item) => {
      const q = searchQuery.trim().toLowerCase();
      const matchesQuery =
        !q ||
        (item.medicineCode ?? '').toLowerCase().includes(q) ||
        item.name.toLowerCase().includes(q) ||
        (item.activeIngredient ?? '').toLowerCase().includes(q) ||
        (item.strength ?? '').toLowerCase().includes(q);

      const matchesPriceStatus =
        !priceStatusFilter ||
        (priceStatusFilter === 'priced' && item.hasEffectivePrice) ||
        (priceStatusFilter === 'unpriced' && !item.hasEffectivePrice);

      const matchesActive =
        !activeFilter ||
        (activeFilter === 'active' && item.isActive) ||
        (activeFilter === 'inactive' && !item.isActive);

      return matchesQuery && matchesPriceStatus && matchesActive;
    });
  }, [pricingQuery.data, searchQuery, priceStatusFilter, activeFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredMedicines.length / pageSize));
  const page = Math.min(currentPage, totalPages);
  const paginatedMedicines = filteredMedicines.slice((page - 1) * pageSize, page * pageSize);

  const handleResetFilters = () => {
    setSearchQuery('');
    setPriceStatusFilter('');
    setActiveFilter('');
    setCurrentPage(1);
  };

  return (
    <AdminPortalShell
      activeTab="Giá thuốc"
      breadcrumbs={[{ label: 'Cấu hình Cơ sở' }, { label: 'Giá thuốc' }]}
    >
      <main className="space-y-6 px-6 py-6 font-sans">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="flex items-center gap-2">
              <span className="rounded-md bg-emerald-100 px-2 py-0.5 text-[11px] font-bold text-emerald-800">
                Quản lý Giá thuốc
              </span>
              <span className="rounded-md bg-sky-100 px-2 py-0.5 text-[11px] font-bold text-primary">
                Hospital Admin
              </span>
            </div>
            <h1 className="mt-1.5 text-xl font-bold text-ink sm:text-2xl">
              Bảng giá thuốc theo cơ sở
            </h1>
            <p className="mt-0.5 text-xs text-sub">
              Niêm yết giá thuốc hiện hành, quản lý lịch sử quyết định giá và điều chỉnh giá theo
              chính sách bệnh viện.
            </p>
          </div>
        </div>

        {pricingQuery.isError && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                {pricingQuery.error instanceof Error
                  ? pricingQuery.error.message
                  : 'Không thể tải bảng giá thuốc của cơ sở y tế.'}
              </span>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => void pricingQuery.refetch()}
              >
                Thử lại
              </Button>
            </div>
          </Alert>
        )}

        <section aria-label="Chỉ số giá thuốc" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                TỔNG THUỐC ĐỊNH GIÁ
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-100 text-primary">
                <Tags className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-ink">{metrics.totalMedicines}</div>
            <div className="mt-1 flex items-center gap-1 text-[11px] font-medium text-emerald-700">
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span>
                {metrics.pricedMedicinesCount} thuốc đã áp dụng giá ({metrics.pricedRatioPercent}%)
              </span>
            </div>
          </div>

          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                GIÁ TRUNG BÌNH
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-100 text-emerald-700">
                <DollarSign className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-ink">
              {formatVnd(metrics.averagePrice)}
            </div>
            <div className="mt-1 text-[11px] text-sub">Giá thuốc trung bình toàn viện</div>
          </div>

          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                KHOẢNG GIÁ NIÊM YẾT
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-100 text-amber-700">
                <TrendingUp className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 truncate text-base font-bold text-ink">
              {formatVnd(metrics.minPrice)} - {formatVnd(metrics.maxPrice)}
            </div>
            <div className="mt-1 text-[11px] text-sub">Tối thiểu đến tối đa</div>
          </div>

          <div className="rounded-xl border border-border bg-white p-4 shadow-xs">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-sub">
                HIỆU LỰC GẦN NHẤT
              </span>
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-100 text-indigo-700">
                <CalendarClock className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <div className="mt-2 truncate font-mono text-base font-bold text-ink">
              {metrics.latestEffectiveDate ?? 'Chưa cập nhật'}
            </div>
            <div className="mt-1 text-[11px] text-sub">Ngày áp dụng giá gần nhất</div>
          </div>
        </section>

        <section
          aria-label="Bộ lọc giá thuốc"
          className="space-y-3 rounded-xl border border-border bg-white p-4 shadow-xs"
        >
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="relative lg:col-span-2">
              <Search
                className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
                aria-hidden="true"
              />
              <input
                id="medicine-pricing-search-input"
                type="text"
                value={searchQuery}
                onChange={(e) => {
                  setSearchQuery(e.target.value);
                  setCurrentPage(1);
                }}
                placeholder="Tìm mã, tên hoặc hoạt chất thuốc..."
                className="w-full rounded-lg border border-border bg-slate-50 py-2 pl-9 pr-3 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none"
              />
            </div>

            <div>
              <select
                id="medicine-pricing-price-status-filter"
                value={priceStatusFilter}
                onChange={(e) => {
                  setPriceStatusFilter(e.target.value as typeof priceStatusFilter);
                  setCurrentPage(1);
                }}
                className="w-full rounded-lg border border-border bg-slate-50 px-3 py-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
              >
                <option value="">Tất cả trạng thái giá</option>
                <option value="priced">Đã có giá hiệu lực</option>
                <option value="unpriced">Chưa có giá</option>
              </select>
            </div>

            <div>
              <select
                id="medicine-pricing-active-filter"
                value={activeFilter}
                onChange={(e) => {
                  setActiveFilter(e.target.value as typeof activeFilter);
                  setCurrentPage(1);
                }}
                className="w-full rounded-lg border border-border bg-slate-50 px-3 py-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
              >
                <option value="">Tất cả trạng thái áp dụng</option>
                <option value="active">Đang áp dụng</option>
                <option value="inactive">Ngừng áp dụng</option>
              </select>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-2 text-xs">
            <div className="text-sub">
              Hiển thị <strong>{filteredMedicines.length}</strong> /{' '}
              <strong>{medicines.length}</strong> thuốc
            </div>
            <button
              type="button"
              onClick={handleResetFilters}
              className="inline-flex cursor-pointer items-center gap-1 text-[11px] font-medium text-slate-500 transition-colors hover:text-primary"
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đặt lại bộ lọc</span>
            </button>
          </div>
        </section>

        <section
          aria-label="Danh sách giá thuốc"
          className="overflow-hidden rounded-xl border border-border bg-white shadow-xs"
        >
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1100px] table-fixed text-left text-xs">
              <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wider text-sub">
                <tr>
                  <th className="w-[10%] px-4 py-3.5">MÃ THUỐC</th>
                  <th className="w-[28%] px-4 py-3.5">TÊN THUỐC</th>
                  <th className="w-[10%] px-4 py-3.5">ĐƠN VỊ</th>
                  <th className="w-[12%] px-4 py-3.5">GIÁ HIỆN HÀNH</th>
                  <th className="w-[16%] px-4 py-3.5">HIỆU LỰC</th>
                  <th className="w-[10%] px-4 py-3.5">TRẠNG THÁI</th>
                  <th className="w-[14%] px-4 py-3.5 pr-6 text-right">THAO TÁC</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {paginatedMedicines.map((medicine) => (
                  <tr key={medicine.id} className="transition-colors hover:bg-sky-50/30">
                    <td className="px-4 py-3.5 font-mono text-xs font-bold text-slate-800">
                      {medicine.medicineCode ?? `#${medicine.id}`}
                    </td>

                    <td className="px-4 py-3.5">
                      <div className="font-bold leading-snug text-ink">{medicine.name}</div>
                      <div className="mt-0.5 line-clamp-1 text-[11px] text-slate-500">
                        {[medicine.activeIngredient, medicine.strength, medicine.dosageForm]
                          .filter((value): value is string => Boolean(value))
                          .join(' • ') || 'Chưa cập nhật hoạt chất'}
                      </div>
                    </td>

                    <td className="px-4 py-3.5 font-medium text-slate-700">
                      {medicine.dispensingUnit ?? '—'}
                    </td>

                    <td className="px-4 py-3.5">
                      {medicine.hasEffectivePrice ? (
                        <span className="text-sm font-bold text-emerald-800">
                          {medicine.priceFormatted || formatVnd(medicine.price)}
                        </span>
                      ) : (
                        <span className="text-[11px] font-medium text-slate-400">Chưa có giá</span>
                      )}
                    </td>

                    <td className="px-4 py-3.5">
                      <div className="font-mono text-[11px] font-semibold text-slate-700">
                        {medicine.validFrom ? medicine.validFrom.slice(0, 10) : '—'}
                      </div>
                      <div className="mt-0.5 font-mono text-[10px] text-slate-400">
                        đến {medicine.validTo ? medicine.validTo.slice(0, 10) : '—'}
                      </div>
                    </td>

                    <td className="px-4 py-3.5">
                      <span
                        className={`inline-block rounded-full px-2.5 py-0.5 text-[10px] font-semibold ${
                          medicine.isActive
                            ? 'bg-emerald-100 text-emerald-800'
                            : 'bg-slate-100 text-slate-600'
                        }`}
                      >
                        {medicine.isActive ? 'Đang áp dụng' : 'Ngừng áp dụng'}
                      </span>
                    </td>

                    <td className="px-4 py-3.5 pr-6 text-right">
                      <div className="flex items-center justify-end gap-1.5 whitespace-nowrap">
                        {canUpdatePrice && (
                          <Button
                            size="sm"
                            onClick={() => setUpdatePriceMedicine(medicine)}
                            className="h-7 gap-1 whitespace-nowrap px-2.5 text-[11px]"
                            title={`Cập nhật giá cho ${medicine.name}`}
                            aria-label={`Cập nhật giá ${medicine.name}`}
                          >
                            <DollarSign className="h-3 w-3" aria-hidden="true" />
                            <span>Cập nhật giá</span>
                          </Button>
                        )}
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => setPriceHistoryMedicine(medicine)}
                          className="h-7 gap-1 px-2 text-[11px] text-sub hover:text-ink"
                          title={`Xem lịch sử giá của ${medicine.name}`}
                          aria-label={`Xem lịch sử giá ${medicine.name}`}
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

          {pricingQuery.isPending && (
            <div className="py-12 text-center text-xs text-sub">Đang tải bảng giá thuốc...</div>
          )}

          {!pricingQuery.isPending && filteredMedicines.length === 0 && (
            <div className="py-16 text-center text-sub">
              <Pill className="mx-auto h-8 w-8 text-slate-300" aria-hidden="true" />
              <p className="mt-2 text-sm font-semibold text-slate-700">
                Không tìm thấy thuốc phù hợp với bộ lọc.
              </p>
              <p className="mt-1 text-xs text-slate-400">
                Hãy thử kiểm tra lại từ khóa tìm kiếm hoặc bấm đặt lại bộ lọc.
              </p>
              <Button variant="secondary" size="sm" onClick={handleResetFilters} className="mt-4">
                Đặt lại bộ lọc
              </Button>
            </div>
          )}

          {filteredMedicines.length > pageSize && (
            <div className="flex items-center justify-between border-t border-border bg-slate-50/50 px-6 py-3 text-xs">
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

      <MedicinePriceUpdateModal />
      <MedicinePriceHistoryModal />
    </AdminPortalShell>
  );
}
