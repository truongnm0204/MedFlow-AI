import { FileSpreadsheet, HelpCircle, Printer, RotateCcw, Search, X } from 'lucide-react';
import type { RefObject } from 'react';

import {
  PRICE_RANGE_OPTIONS,
  PRICING_CATEGORIES,
  PRICING_DEPARTMENTS,
  PRICING_STATUS_OPTIONS,
} from '../data/service-pricing-mock';
import type { PriceRangeFilter, ServicePricingFilter } from '../types/service-pricing.types';

interface PricingFiltersProps {
  filters: ServicePricingFilter;
  onChange: (partial: Partial<ServicePricingFilter>) => void;
  onReset: () => void;
  onExportCsv: () => void;
  onPrint: () => void;
  onOpenPolicy: () => void;
  searchInputRef: RefObject<HTMLInputElement | null>;
  totalCount: number;
}

export function PricingFilters({
  filters,
  onChange,
  onReset,
  onExportCsv,
  onPrint,
  onOpenPolicy,
  searchInputRef,
  totalCount,
}: PricingFiltersProps) {
  const isFiltered =
    Boolean(filters.search.trim()) ||
    (filters.department !== 'Tất cả khoa phòng' && filters.department !== 'all') ||
    filters.category !== 'all' ||
    filters.priceRange !== 'all' ||
    filters.status !== 'all';

  return (
    <div className="space-y-3.5 rounded-xl border border-border bg-white p-4 shadow-xs">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        {/* Search Input */}
        <div className="relative flex-1">
          <Search
            className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
            aria-hidden="true"
          />
          <input
            ref={searchInputRef}
            type="text"
            value={filters.search}
            onChange={(e) => onChange({ search: e.target.value })}
            placeholder="Tìm theo mã DV (EX-*), tên dịch vụ, số quyết định (QĐ-*)..."
            aria-label="Tìm kiếm dịch vụ và biểu phí"
            className="h-9 w-full rounded-lg border border-border bg-slate-50/50 pl-9 pr-16 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none focus:ring-1 focus:ring-primary"
          />
          {filters.search ? (
            <button
              type="button"
              onClick={() => onChange({ search: '' })}
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

        {/* Action Buttons */}
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={onOpenPolicy}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            title="Xem quy chuẩn định giá & cơ sở pháp lý UC52"
          >
            <HelpCircle className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Quy định UC52</span>
          </button>

          <button
            type="button"
            onClick={onExportCsv}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            title="Xuất danh mục biểu phí ra file CSV (Excel)"
          >
            <FileSpreadsheet className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
            <span>Xuất CSV</span>
          </button>

          <button
            type="button"
            onClick={onPrint}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            title="In bảng niêm yết giá dịch vụ"
          >
            <Printer className="h-3.5 w-3.5 text-slate-600" aria-hidden="true" />
            <span>In biểu phí</span>
          </button>
        </div>
      </div>

      {/* Dropdown Filters Grid */}
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
        {/* Department Select */}
        <div>
          <label htmlFor="filter-department" className="sr-only">
            Khoa phòng phụ trách
          </label>
          <select
            id="filter-department"
            value={filters.department}
            onChange={(e) => onChange({ department: e.target.value })}
            className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          >
            {PRICING_DEPARTMENTS.map((dept) => (
              <option key={dept} value={dept}>
                {dept}
              </option>
            ))}
          </select>
        </div>

        {/* Category Select */}
        <div>
          <label htmlFor="filter-category" className="sr-only">
            Phân loại dịch vụ
          </label>
          <select
            id="filter-category"
            value={filters.category}
            onChange={(e) => onChange({ category: e.target.value })}
            className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          >
            {PRICING_CATEGORIES.map((cat) => (
              <option key={cat.value} value={cat.value}>
                {cat.label}
              </option>
            ))}
          </select>
        </div>

        {/* Price Range Select */}
        <div>
          <label htmlFor="filter-price-range" className="sr-only">
            Khoảng giá
          </label>
          <select
            id="filter-price-range"
            value={filters.priceRange}
            onChange={(e) => onChange({ priceRange: e.target.value as PriceRangeFilter })}
            className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          >
            {PRICE_RANGE_OPTIONS.map((rng) => (
              <option key={rng.value} value={rng.value}>
                {rng.label}
              </option>
            ))}
          </select>
        </div>

        {/* Status Select */}
        <div>
          <label htmlFor="filter-status" className="sr-only">
            Trạng thái áp dụng
          </label>
          <select
            id="filter-status"
            value={filters.status}
            onChange={(e) => onChange({ status: e.target.value })}
            className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          >
            {PRICING_STATUS_OPTIONS.map((st) => (
              <option key={st.value} value={st.value}>
                {st.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Filter Info / Reset */}
      <div className="flex items-center justify-between border-t border-slate-100 pt-2.5 text-xs text-sub">
        <div>
          Tìm thấy <strong className="text-ink">{totalCount}</strong> dịch vụ trong danh mục biểu
          phí
        </div>
        {isFiltered && (
          <button
            type="button"
            onClick={onReset}
            className="inline-flex items-center gap-1 font-semibold text-primary transition-colors hover:text-primary-dark"
          >
            <RotateCcw className="h-3 w-3" aria-hidden="true" />
            <span>Đặt lại bộ lọc</span>
          </button>
        )}
      </div>
    </div>
  );
}
