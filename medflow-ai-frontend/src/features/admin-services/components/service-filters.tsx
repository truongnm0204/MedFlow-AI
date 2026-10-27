import { Download, Plus, RotateCcw, Search, SlidersHorizontal } from 'lucide-react';
import { type RefObject } from 'react';
import { Link } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { CATEGORIES, DEPARTMENTS } from '../data/examination-services-data';
import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ExaminationService } from '../types/examination-service.types';

interface ServiceFiltersProps {
  searchRef: RefObject<HTMLInputElement | null>;
  onExportCsv: (services: ExaminationService[]) => void;
  filteredServices: ExaminationService[];
}

export function ServiceFilters({ searchRef, onExportCsv, filteredServices }: ServiceFiltersProps) {
  const searchQuery = useExaminationServiceStore((state) => state.searchQuery);
  const categoryFilter = useExaminationServiceStore((state) => state.categoryFilter);
  const departmentFilter = useExaminationServiceStore((state) => state.departmentFilter);
  const bookingFilter = useExaminationServiceStore((state) => state.bookingFilter);
  const statusFilter = useExaminationServiceStore((state) => state.statusFilter);

  const setSearchQuery = useExaminationServiceStore((state) => state.setSearchQuery);
  const setCategoryFilter = useExaminationServiceStore((state) => state.setCategoryFilter);
  const setDepartmentFilter = useExaminationServiceStore((state) => state.setDepartmentFilter);
  const setBookingFilter = useExaminationServiceStore((state) => state.setBookingFilter);
  const setStatusFilter = useExaminationServiceStore((state) => state.setStatusFilter);
  const resetFilters = useExaminationServiceStore((state) => state.resetFilters);
  const setIsColumnModalOpen = useExaminationServiceStore((state) => state.setIsColumnModalOpen);

  return (
    <section
      className="space-y-3 rounded-lg border border-border bg-white p-4 shadow-sm"
      aria-label="Tìm kiếm và bộ lọc dịch vụ khám"
    >
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex min-w-80 flex-1 items-center gap-2.5 rounded-lg border border-border bg-canvas px-3.5 py-2.5 transition-colors focus-within:border-primary focus-within:bg-white focus-within:ring-1 focus-within:ring-primary">
          <Search className="h-4 w-4 shrink-0 text-slate-400" />
          <input
            ref={searchRef}
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Tìm theo mã, tên dịch vụ hoặc khoa phụ trách..."
            className="min-w-0 flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-slate-400"
            aria-label="Tìm dịch vụ khám"
          />
          <kbd className="hidden rounded border border-border bg-white px-1.5 py-0.5 text-[10px] font-medium text-sub sm:inline-block shadow-xs">
            Ctrl + K
          </kbd>
        </div>

        <Button
          variant="secondary"
          size="sm"
          onClick={() => searchRef.current?.focus()}
          className="gap-2"
        >
          <Search className="h-3.5 w-3.5 text-slate-500" />
          Tìm kiếm
        </Button>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 pt-1 border-t border-slate-100">
        <div className="flex flex-wrap items-center gap-2.5">
          <select
            aria-label="Lọc phân loại"
            value={categoryFilter}
            onChange={(e) => setCategoryFilter(e.target.value)}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
          >
            <option value="">Phân loại: Tất cả</option>
            {CATEGORIES.map((cat) => (
              <option key={cat} value={cat}>
                {cat}
              </option>
            ))}
          </select>

          <select
            aria-label="Lọc khoa phụ trách"
            value={departmentFilter}
            onChange={(e) => setDepartmentFilter(e.target.value)}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
          >
            <option value="">Khoa: Tất cả khoa phòng</option>
            {DEPARTMENTS.map((dept) => (
              <option key={dept} value={dept}>
                {dept}
              </option>
            ))}
          </select>

          <select
            aria-label="Lọc kênh đặt lịch"
            value={bookingFilter}
            onChange={(e) => setBookingFilter(e.target.value)}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
          >
            <option value="">Đặt lịch: Tất cả</option>
            <option value="online">Cho phép Online</option>
            <option value="counter">Chỉ tại quầy</option>
          </select>

          <select
            aria-label="Lọc trạng thái"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
          >
            <option value="">Trạng thái: Tất cả</option>
            <option value="ACTIVE">Hoạt động</option>
            <option value="SUSPENDED">Tạm dừng</option>
          </select>

          <button
            type="button"
            onClick={resetFilters}
            title="Đặt lại bộ lọc"
            aria-label="Đặt lại bộ lọc"
            className="grid h-8 w-8 place-items-center rounded-lg border border-border bg-white text-slate-500 hover:bg-slate-50 hover:text-ink transition-colors shadow-xs"
          >
            <RotateCcw className="h-3.5 w-3.5" />
          </button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => onExportCsv(filteredServices)}
            className="gap-1.5"
            title="Xuất file Excel/CSV danh mục dịch vụ"
          >
            <Download className="h-3.5 w-3.5 text-slate-500" />
            Xuất Excel
          </Button>

          <Button
            variant="secondary"
            size="sm"
            onClick={() => setIsColumnModalOpen(true)}
            className="gap-1.5"
            title="Tùy chỉnh cột hiển thị"
          >
            <SlidersHorizontal className="h-3.5 w-3.5 text-slate-500" />
            Tùy chỉnh cột
          </Button>

          <Link
            to="/admin/services/new"
            className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:bg-primary-dark transition-colors"
          >
            <Plus className="h-4 w-4" />
            <span>Thêm Dịch Vụ Mới</span>
            <kbd className="ml-1 rounded bg-white/20 px-1 py-0.5 text-[10px]">F2</kbd>
          </Link>
        </div>
      </div>
    </section>
  );
}
