import { CalendarPlus, Download, RotateCcw, Search } from 'lucide-react';
import type { RefObject } from 'react';

import { Button } from '@/shared/ui/button';

import { SLOT_DATES, SLOT_SPECIALTIES } from '../data/examination-slots-mock';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

interface SlotFiltersProps {
  searchRef: RefObject<HTMLInputElement | null>;
  onExportCsv: (slots: ExaminationSlot[]) => void;
  filteredSlots: ExaminationSlot[];
  onCreateSlot?: () => void;
}

export function SlotFilters({
  searchRef,
  onExportCsv,
  filteredSlots,
  onCreateSlot,
}: SlotFiltersProps) {
  const searchQuery = useExaminationSlotStore((state) => state.searchQuery);
  const dateFilter = useExaminationSlotStore((state) => state.dateFilter);
  const specialtyFilter = useExaminationSlotStore((state) => state.specialtyFilter);
  const statusFilter = useExaminationSlotStore((state) => state.statusFilter);

  const setSearchQuery = useExaminationSlotStore((state) => state.setSearchQuery);
  const setDateFilter = useExaminationSlotStore((state) => state.setDateFilter);
  const setSpecialtyFilter = useExaminationSlotStore((state) => state.setSpecialtyFilter);
  const setStatusFilter = useExaminationSlotStore((state) => state.setStatusFilter);
  const resetFilters = useExaminationSlotStore((state) => state.resetFilters);

  const hasActiveFilters = Boolean(searchQuery || dateFilter || specialtyFilter || statusFilter);

  return (
    <section
      className="space-y-3 rounded-xl border border-border bg-white p-4 shadow-xs"
      aria-label="Tìm kiếm và bộ lọc khung giờ khám"
    >
      <div className="flex flex-wrap items-center gap-3">
        {/* Search input with Ctrl+K shortcut */}
        <div className="flex min-w-72 flex-1 items-center gap-2.5 rounded-lg border border-border bg-canvas px-3.5 py-2 transition-colors focus-within:border-primary focus-within:bg-white focus-within:ring-1 focus-within:ring-primary">
          <Search className="h-4 w-4 shrink-0 text-slate-400" aria-hidden="true" />
          <input
            ref={searchRef}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Tìm bác sĩ, dịch vụ, ngày khám hoặc phòng..."
            className="min-w-0 flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-slate-400"
            aria-label="Tìm kiếm khung giờ khám"
          />
          <kbd className="hidden rounded border border-border bg-white px-1.5 py-0.5 text-[10px] font-medium text-sub sm:inline-block shadow-2xs">
            Ctrl + K
          </kbd>
        </div>

        {/* Date Filter */}
        <select
          value={dateFilter}
          onChange={(e) => setDateFilter(e.target.value)}
          className="h-9 rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-ink outline-none transition-colors hover:border-slate-300 focus:border-primary focus:ring-1 focus:ring-primary"
          aria-label="Lọc theo ngày khám"
        >
          <option value="">Tất cả ngày khám</option>
          {SLOT_DATES.map((date) => (
            <option key={date.value} value={date.value}>
              {date.label}
            </option>
          ))}
        </select>

        {/* Specialty Filter */}
        <select
          value={specialtyFilter}
          onChange={(e) => setSpecialtyFilter(e.target.value)}
          className="h-9 rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-ink outline-none transition-colors hover:border-slate-300 focus:border-primary focus:ring-1 focus:ring-primary"
          aria-label="Lọc theo chuyên khoa"
        >
          <option value="">Tất cả chuyên khoa</option>
          {SLOT_SPECIALTIES.map((spec) => (
            <option key={spec} value={spec}>
              {spec}
            </option>
          ))}
        </select>

        {/* Status Filter */}
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="h-9 rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-ink outline-none transition-colors hover:border-slate-300 focus:border-primary focus:ring-1 focus:ring-primary"
          aria-label="Lọc theo trạng thái"
        >
          <option value="">Tất cả trạng thái</option>
          <option value="OPEN">Đang mở</option>
          <option value="CLOSED">Đã đóng</option>
        </select>

        {hasActiveFilters && (
          <Button
            variant="ghost"
            size="sm"
            onClick={resetFilters}
            className="h-9 text-xs text-sub hover:text-ink gap-1 px-2.5"
            aria-label="Đặt lại bộ lọc"
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Đặt lại</span>
          </Button>
        )}

        {/* Export Excel Button */}
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onExportCsv(filteredSlots)}
          className="h-9 gap-1.5 px-3 text-xs"
          title="Xuất danh sách khung giờ ra file CSV/Excel"
        >
          <Download className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Xuất Excel</span>
        </Button>

        {/* Create Slot Button */}
        <Button
          variant="primary"
          size="sm"
          onClick={onCreateSlot}
          className="h-9 gap-1.5 px-3 text-xs bg-primary hover:bg-sky-600 text-white shadow-xs font-semibold"
          title="Tạo khung giờ khám mới (F2)"
        >
          <CalendarPlus className="h-4 w-4" aria-hidden="true" />
          <span>+ Tạo khung giờ</span>
          <kbd className="ml-1 rounded bg-white/20 px-1 py-0.5 text-[10px] font-mono font-bold text-white">
            F2
          </kbd>
        </Button>
      </div>
    </section>
  );
}
