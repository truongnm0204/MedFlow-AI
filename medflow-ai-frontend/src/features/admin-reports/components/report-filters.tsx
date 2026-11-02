import { Calendar, Download, FileDown, RotateCcw, Search, SlidersHorizontal } from 'lucide-react';
import type { RefObject } from 'react';

import { Button } from '@/shared/ui/button';

import {
  REPORT_CATEGORIES,
  REPORT_DATE_OPTIONS,
  REPORT_FACILITIES,
} from '../data/operational-reports-data';
import { useOperationalReportStore } from '../store/operational-report.store';
import type { OperationalReportItem } from '../types/operational-report.types';

interface ReportFiltersProps {
  searchRef: RefObject<HTMLInputElement | null>;
  filteredReports: OperationalReportItem[];
  onExportCsv: (reports: OperationalReportItem[]) => void;
}

export function ReportFilters({ searchRef, filteredReports, onExportCsv }: ReportFiltersProps) {
  const searchQuery = useOperationalReportStore((state) => state.searchQuery);
  const periodFilter = useOperationalReportStore((state) => state.periodFilter);
  const facilityFilter = useOperationalReportStore((state) => state.facilityFilter);
  const categoryFilter = useOperationalReportStore((state) => state.categoryFilter);

  const setSearchQuery = useOperationalReportStore((state) => state.setSearchQuery);
  const setPeriodFilter = useOperationalReportStore((state) => state.setPeriodFilter);
  const setFacilityFilter = useOperationalReportStore((state) => state.setFacilityFilter);
  const setCategoryFilter = useOperationalReportStore((state) => state.setCategoryFilter);
  const resetFilters = useOperationalReportStore((state) => state.resetFilters);

  const hasActiveFilters =
    Boolean(searchQuery) ||
    periodFilter !== '22/10/2026' ||
    Boolean(facilityFilter) ||
    Boolean(categoryFilter);

  return (
    <section
      className="rounded-lg border border-border bg-white p-4 shadow-sm"
      aria-label="Tìm kiếm và bộ lọc báo cáo vận hành"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-72 flex-1 items-center gap-2.5 rounded-lg border border-border bg-canvas px-3.5 py-2.5 transition-colors focus-within:border-primary focus-within:bg-white focus-within:ring-1 focus-within:ring-primary">
          <Search className="h-4 w-4 shrink-0 text-slate-400" aria-hidden="true" />
          <input
            ref={searchRef}
            type="text"
            value={searchQuery}
            onChange={(event) => setSearchQuery(event.target.value)}
            placeholder="Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp..."
            className="min-w-0 flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-slate-400"
            aria-label="Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp"
          />
          <kbd className="hidden rounded border border-border bg-white px-1.5 py-0.5 font-mono text-[10px] font-medium text-sub shadow-xs sm:inline-block">
            Ctrl + K
          </kbd>
        </div>

        <div className="flex flex-wrap items-center gap-2.5">
          <div className="relative flex items-center">
            <Calendar
              className="pointer-events-none absolute left-3 h-3.5 w-3.5 text-sub"
              aria-hidden="true"
            />
            <select
              value={periodFilter}
              onChange={(event) => setPeriodFilter(event.target.value)}
              className="h-10 rounded-lg border border-border bg-white pl-8 pr-3 text-xs font-medium text-ink shadow-xs outline-none focus:border-primary"
              aria-label="Lọc kỳ báo cáo"
            >
              {REPORT_DATE_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>

          <select
            value={facilityFilter}
            onChange={(event) => setFacilityFilter(event.target.value)}
            className="h-10 rounded-lg border border-border bg-white px-3 text-xs font-medium text-ink shadow-xs outline-none focus:border-primary"
            aria-label="Lọc cơ sở"
          >
            <option value="">Tất cả cơ sở</option>
            {REPORT_FACILITIES.map((facility) => (
              <option key={facility} value={facility}>
                {facility}
              </option>
            ))}
          </select>

          <select
            value={categoryFilter}
            onChange={(event) => setCategoryFilter(event.target.value)}
            className="h-10 rounded-lg border border-border bg-white px-3 text-xs font-medium text-ink shadow-xs outline-none focus:border-primary"
            aria-label="Lọc loại báo cáo"
          >
            <option value="">Tất cả báo cáo</option>
            {REPORT_CATEGORIES.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>

          {hasActiveFilters && (
            <Button
              variant="ghost"
              size="sm"
              onClick={resetFilters}
              className="h-10 gap-1.5 px-2.5 text-xs text-sub hover:text-ink"
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              Đặt lại
            </Button>
          )}

          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => onExportCsv(filteredReports)}
              className="h-10 gap-1.5 px-3 text-xs"
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Xuất Excel</span>
            </Button>

            <Button
              variant="secondary"
              size="sm"
              disabled
              className="h-10 px-2.5"
              aria-label="Tùy chọn cột hiển thị"
              title="Tùy chọn cột hiển thị"
            >
              <SlidersHorizontal className="h-4 w-4" aria-hidden="true" />
            </Button>

            <Button
              variant="primary"
              size="sm"
              onClick={() => onExportCsv(filteredReports)}
              className="h-10 gap-2 px-3.5 text-xs font-bold shadow-sm"
            >
              <FileDown className="h-4 w-4" aria-hidden="true" />
              <span>Xuất báo cáo</span>
              <kbd className="rounded bg-white/20 px-1.5 py-0.5 font-mono text-[10px]">F2</kbd>
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}
