import { Calendar, Download, FileDown, RefreshCw, RotateCcw, Search } from 'lucide-react';
import type { RefObject } from 'react';

import { Button } from '@/shared/ui/button';

import type { OperationalReportDepartmentOption } from '../api/operational-report.api';
import type { OperationalReportItem } from '../types/operational-report.types';

interface ReportFiltersProps {
  searchRef: RefObject<HTMLInputElement | null>;
  searchQuery: string;
  onSearchChange: (val: string) => void;
  fromDate: string;
  toDate: string;
  onFromDateChange: (val: string) => void;
  onToDateChange: (val: string) => void;
  departmentId: string;
  onDepartmentChange: (val: string) => void;
  departments: OperationalReportDepartmentOption[];
  hasActiveFilters: boolean;
  onResetFilters: () => void;
  onRefresh: () => void;
  filteredReports: OperationalReportItem[];
  onExportCsv: (reports: OperationalReportItem[]) => void;
}

export function ReportFilters({
  searchRef,
  searchQuery,
  onSearchChange,
  fromDate,
  toDate,
  onFromDateChange,
  onToDateChange,
  departmentId,
  onDepartmentChange,
  departments,
  hasActiveFilters,
  onResetFilters,
  onRefresh,
  filteredReports,
  onExportCsv,
}: ReportFiltersProps) {
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
            onChange={(event) => onSearchChange(event.target.value)}
            placeholder="Tìm tên báo cáo, mã khoa hoặc kỳ tổng hợp..."
            className="min-w-0 flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-slate-400"
            aria-label="Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp"
          />
          <kbd className="hidden rounded border border-border bg-white px-1.5 py-0.5 font-mono text-[10px] font-medium text-sub shadow-xs sm:inline-block">
            Ctrl + K
          </kbd>
        </div>

        <div className="flex flex-wrap items-center gap-2.5">
          <div className="flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs">
            <Calendar className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
            <input
              type="date"
              value={fromDate}
              onChange={(e) => onFromDateChange(e.target.value)}
              aria-label="Từ ngày"
              className="bg-transparent font-mono text-xs text-ink outline-none"
            />
            <span className="text-sub">→</span>
            <input
              type="date"
              value={toDate}
              onChange={(e) => onToDateChange(e.target.value)}
              aria-label="Đến ngày"
              className="bg-transparent font-mono text-xs text-ink outline-none"
            />
          </div>

          <select
            value={departmentId}
            onChange={(event) => onDepartmentChange(event.target.value)}
            className="h-10 rounded-lg border border-border bg-white px-3 text-xs font-medium text-ink shadow-xs outline-none focus:border-primary"
            aria-label="Lọc khoa phòng"
          >
            <option value="">Tất cả khoa phòng</option>
            {departments.map((dept) => (
              <option key={dept.id} value={String(dept.id)}>
                {dept.name} ({dept.code})
              </option>
            ))}
          </select>

          {hasActiveFilters && (
            <Button
              variant="ghost"
              size="sm"
              onClick={onResetFilters}
              className="h-10 gap-1.5 px-2.5 text-xs text-sub hover:text-ink"
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đặt lại</span>
            </Button>
          )}

          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={onRefresh}
              className="h-10 gap-1.5 px-3 text-xs"
            >
              <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Làm mới</span>
            </Button>

            <Button
              variant="secondary"
              size="sm"
              onClick={() => onExportCsv(filteredReports)}
              className="h-10 gap-1.5 px-3 text-xs"
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Xuất CSV</span>
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
