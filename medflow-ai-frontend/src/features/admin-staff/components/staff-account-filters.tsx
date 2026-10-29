import { Download, Plus, Search } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import {
  STAFF_DEPARTMENTS,
  STAFF_ROLES,
  STAFF_STATUS_LABELS,
} from '../data/staff-account-mock-data';
import type { StaffAccountFilters } from '../types/staff-account.types';

interface StaffAccountFiltersProps {
  filters: StaffAccountFilters;
  onChange: (patch: Partial<StaffAccountFilters>) => void;
  onExport: () => void;
  onAdd: () => void;
}

const SELECT_CLASS =
  'h-10 rounded-lg border border-border bg-white px-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20';

export function StaffAccountFilterBar({
  filters,
  onChange,
  onExport,
  onAdd,
}: StaffAccountFiltersProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative min-w-[260px] flex-1">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
          aria-hidden="true"
        />
        <input
          type="search"
          value={filters.query}
          onChange={(event) => onChange({ query: event.target.value })}
          placeholder="Tìm mã nhân sự, họ tên hoặc email..."
          aria-label="Tìm kiếm tài khoản nhân sự"
          className="h-10 w-full rounded-lg border border-border bg-white pl-9 pr-3 text-sm text-navy placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
        />
      </div>
      <select
        aria-label="Lọc theo khoa phòng"
        value={filters.department}
        onChange={(event) => onChange({ department: event.target.value })}
        className={SELECT_CLASS}
      >
        <option value="">Tất cả khoa phòng</option>
        {STAFF_DEPARTMENTS.map((department) => (
          <option key={department} value={department}>
            {department}
          </option>
        ))}
      </select>
      <select
        aria-label="Lọc theo vai trò"
        value={filters.role}
        onChange={(event) => onChange({ role: event.target.value })}
        className={SELECT_CLASS}
      >
        <option value="">Tất cả vai trò</option>
        {STAFF_ROLES.map((role) => (
          <option key={role} value={role}>
            {role}
          </option>
        ))}
      </select>
      <select
        aria-label="Lọc theo trạng thái"
        value={filters.status}
        onChange={(event) => onChange({ status: event.target.value })}
        className={SELECT_CLASS}
      >
        <option value="">Tất cả trạng thái</option>
        {Object.entries(STAFF_STATUS_LABELS).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <Button type="button" variant="secondary" aria-label="Xuất Excel" onClick={onExport}>
        <Download className="h-4 w-4" aria-hidden="true" />
        Xuất Excel
      </Button>
      <Button type="button" aria-label="Thêm tài khoản" onClick={onAdd}>
        <Plus className="h-4 w-4" aria-hidden="true" />
        Thêm tài khoản
      </Button>
    </div>
  );
}
