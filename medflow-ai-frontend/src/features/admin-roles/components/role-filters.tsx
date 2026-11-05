import { Download, Plus, Search } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import { ROLE_SCOPE_LABELS, ROLE_STATUS_LABELS } from '../data/role-mock-data';
import type { RoleFilters, RoleScope, RoleStatus } from '../types/role.types';

interface RoleFilterBarProps {
  filters: RoleFilters;
  onChange: (patch: Partial<RoleFilters>) => void;
  onExport: () => void;
  onAdd?: () => void;
}

const SELECT_CLASS =
  'h-10 rounded-lg border border-border bg-white px-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20';

export function RoleFilterBar({ filters, onChange, onExport, onAdd }: RoleFilterBarProps) {
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
          placeholder="Tìm mã vai trò, tên vai trò hoặc quyền hạn..."
          aria-label="Tìm kiếm vai trò"
          className="h-10 w-full rounded-lg border border-border bg-white pl-9 pr-3 text-sm text-navy placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
        />
      </div>
      <select
        aria-label="Lọc theo nhóm vai trò"
        value={filters.scope}
        onChange={(event) => onChange({ scope: event.target.value as '' | RoleScope })}
        className={SELECT_CLASS}
      >
        <option value="">Tất cả nhóm vai trò</option>
        {Object.entries(ROLE_SCOPE_LABELS).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <select
        aria-label="Lọc theo trạng thái"
        value={filters.status}
        onChange={(event) => onChange({ status: event.target.value as '' | RoleStatus })}
        className={SELECT_CLASS}
      >
        <option value="">Tất cả trạng thái</option>
        {Object.entries(ROLE_STATUS_LABELS).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <Button type="button" variant="secondary" aria-label="Xuất Excel" onClick={onExport}>
        <Download className="h-4 w-4" aria-hidden="true" />
        Xuất Excel
      </Button>
      {onAdd && (
        <Button type="button" aria-label="Thêm vai trò" onClick={onAdd}>
          <Plus className="h-4 w-4" aria-hidden="true" />
          Thêm vai trò
        </Button>
      )}
    </div>
  );
}
