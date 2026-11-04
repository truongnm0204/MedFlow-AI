import { FileSpreadsheet, Printer, RotateCcw, Search, X } from 'lucide-react';
import type { RefObject } from 'react';

import { PRICING_CATEGORIES, PRICING_DEPARTMENTS } from '../data/service-pricing-mock';
import type { PriceRangeFilter, ServicePricingFilter } from '../types/service-pricing.types';

const PRICE_RANGE_OPTIONS: { label: string; value: PriceRangeFilter }[] = [
  { label: 'Tất cả mức giá', value: 'all' },
  { label: 'Dưới 200.000 đ', value: 'under-200k' },
  { label: '200.000 đ - 500.000 đ', value: '200k-500k' },
  { label: 'Trên 500.000 đ', value: 'over-500k' },
];

const PRICING_STATUS_OPTIONS = [
  { label: 'Tất cả trạng thái', value: 'all' },
  { label: 'Đang áp dụng', value: 'ACTIVE' },
  { label: 'Tạm dừng niêm yết', value: 'SUSPENDED' },
];

interface PricingFiltersProps {
  filters: ServicePricingFilter;
  onChange: (partial: Partial<ServicePricingFilter>) => void;
  onReset: () => void;
  onExportCsv: () => void;
  onPrint: () => void;
  searchInputRef: RefObject<HTMLInputElement | null>;
  totalCount: number;
}

export function PricingFilters({
  filters,
  onChange,
  onReset,
  onExportCsv,
  onPrint,
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
    <div className="space-y-3 rounded-xl border border-border bg-white p-4 shadow-xs">
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
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onExportCsv}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 transition-colors"
          >
            <FileSpreadsheet className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
            <span>Xuất CSV</span>
          </button>
          <button
            type="button"
            onClick={onPrint}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 transition-colors"
          >
            <Printer className="h-3.5 w-3.5 text-slate-600" aria-hidden="true" />
            <span>In biểu phí</span>
          </button>
        </div>
      </div>

      {/* Dropdown Filters Grid */}
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
        <select
          aria-label="Khoa phòng phụ trách"
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

        <select
          aria-label="Phân loại dịch vụ"
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

        <select
          aria-label="Khoảng giá"
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

        <select
          aria-label="Trạng thái áp dụng"
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

      {/* Filter Info / Reset */}
      <div className="flex items-center justify-between border-t border-slate-100 pt-2 text-xs text-sub">
        <span>
          Tìm thấy <strong className="text-ink">{totalCount}</strong> dịch vụ trong danh mục
        </span>
        {isFiltered && (
          <button
            type="button"
            onClick={onReset}
            className="inline-flex items-center gap-1 font-semibold text-primary hover:text-primary-dark"
          >
            <RotateCcw className="h-3 w-3" aria-hidden="true" />
            <span>Đặt lại bộ lọc</span>
          </button>
        )}
      </div>
    </div>
  );
}
