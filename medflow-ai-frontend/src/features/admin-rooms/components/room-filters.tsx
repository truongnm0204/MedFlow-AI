import { Download, Plus, Search } from 'lucide-react';
import type { Ref } from 'react';

import { ROOM_STATUS_LABELS } from '../types/room.types';
import type { RoomFilters as RoomFiltersState, RoomStatus } from '../types/room.types';

interface RoomFiltersProps {
  filters: RoomFiltersState;
  departments: string[];
  searchRef?: Ref<HTMLInputElement>;
  onChange: (filters: RoomFiltersState) => void;
  onExport: () => void;
  onAdd: () => void;
}

const controlClass =
  'h-10 rounded-lg border border-border bg-white px-3 text-xs text-ink focus:border-primary focus:outline-none';

export function RoomFilters({
  filters,
  departments,
  searchRef,
  onChange,
  onExport,
  onAdd,
}: RoomFiltersProps) {
  return (
    <section
      aria-label="Bộ lọc buồng khám"
      className="flex flex-wrap items-center gap-3 rounded-xl border border-border bg-white p-3 shadow-sm"
    >
      <div className="relative min-w-[260px] flex-1">
        <Search
          className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
          aria-hidden="true"
        />
        <input
          ref={searchRef}
          type="search"
          value={filters.searchQuery}
          onChange={(e) => onChange({ ...filters, searchQuery: e.target.value })}
          placeholder="Tìm theo mã phòng hoặc tên phòng..."
          aria-label="Tìm kiếm buồng khám"
          className={`${controlClass} w-full pl-9`}
        />
      </div>

      <select
        value={filters.department}
        onChange={(e) => onChange({ ...filters, department: e.target.value })}
        aria-label="Lọc theo khoa phòng"
        className={controlClass}
      >
        <option value="">Tất cả khoa phòng</option>
        {departments.map((d) => (
          <option key={d} value={d}>
            {d}
          </option>
        ))}
      </select>

      <select
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value as RoomStatus | '' })}
        aria-label="Lọc theo trạng thái"
        className={controlClass}
      >
        <option value="">Tất cả trạng thái</option>
        {(Object.keys(ROOM_STATUS_LABELS) as RoomStatus[]).map((s) => (
          <option key={s} value={s}>
            {ROOM_STATUS_LABELS[s]}
          </option>
        ))}
      </select>

      <button
        type="button"
        onClick={onExport}
        aria-label="Xuất Excel"
        className="flex h-10 items-center gap-1.5 rounded-lg border border-border bg-white px-3 text-xs font-semibold text-ink hover:bg-slate-50"
      >
        <Download className="h-4 w-4" aria-hidden="true" />
        Xuất Excel
      </button>

      <button
        type="button"
        onClick={onAdd}
        aria-label="Thêm buồng khám"
        className="flex h-10 items-center gap-1.5 rounded-lg bg-primary px-3 text-xs font-semibold text-white hover:bg-primary-dark"
      >
        <Plus className="h-4 w-4" aria-hidden="true" />
        Thêm buồng khám
      </button>
    </section>
  );
}
