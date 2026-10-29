import { Download, Plus, Search } from 'lucide-react';

import type { DepartmentFilters as Filters } from '../types/department.types';

interface DepartmentFiltersProps {
  filters: Filters;
  facilities: string[];
  onChange: (next: Filters) => void;
  onExport: () => void;
}

const controlClass =
  'h-10 rounded-lg border border-border bg-white px-3 text-sm text-ink focus:border-primary focus:outline-none';

export function DepartmentFilters({
  filters,
  facilities,
  onChange,
  onExport,
}: DepartmentFiltersProps) {
  return (
    <section
      aria-label="Bộ lọc khoa phòng"
      className="flex flex-wrap items-center gap-3 rounded-xl border border-border bg-white p-4 shadow-sm"
    >
      <div className="relative min-w-[260px] flex-1">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
          aria-hidden="true"
        />
        <input
          type="search"
          value={filters.search}
          onChange={(e) => onChange({ ...filters, search: e.target.value })}
          placeholder="Tìm theo mã khoa hoặc tên khoa..."
          aria-label="Tìm theo mã khoa hoặc tên khoa"
          className={`${controlClass} w-full pl-9`}
        />
      </div>
      <select
        value={filters.facility}
        onChange={(e) => onChange({ ...filters, facility: e.target.value })}
        aria-label="Lọc theo cơ sở"
        className={controlClass}
      >
        <option value="">Tất cả cơ sở</option>
        {facilities.map((f) => (
          <option key={f} value={f}>
            {f}
          </option>
        ))}
      </select>
      <select
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value as Filters['status'] })}
        aria-label="Lọc theo trạng thái"
        className={controlClass}
      >
        <option value="">Tất cả trạng thái</option>
        <option value="ACTIVE">Đang hoạt động</option>
        <option value="INACTIVE">Tạm ngừng</option>
      </select>
      <button
        type="button"
        onClick={onExport}
        aria-label="Xuất Excel"
        className="inline-flex h-10 items-center gap-2 rounded-lg border border-border bg-white px-4 text-sm font-medium text-navy hover:bg-canvas"
      >
        <Download className="h-4 w-4" aria-hidden="true" />
        Xuất Excel
      </button>
      <button
        type="button"
        aria-label="Thêm khoa"
        className="inline-flex h-10 items-center gap-2 rounded-lg bg-primary px-4 text-sm font-semibold text-white hover:bg-primary-dark"
      >
        <Plus className="h-4 w-4" aria-hidden="true" />
        Thêm khoa
      </button>
    </section>
  );
}
