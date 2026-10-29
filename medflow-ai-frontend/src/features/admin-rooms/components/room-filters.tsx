import { Download, Plus, Search } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import type { RoomFilters as RoomFiltersType, RoomItem } from '../types/room.types';

interface RoomFiltersProps {
  filters: RoomFiltersType;
  onChange: (filters: Partial<RoomFiltersType>) => void;
  departments: string[];
  rooms: RoomItem[];
  onAddRoom?: () => void;
}

export function RoomFilters({
  filters,
  onChange,
  departments,
  rooms,
  onAddRoom,
}: RoomFiltersProps) {
  const handleExportCsv = () => {
    const headers = ['Mã phòng', 'Tên buồng khám', 'Khoa phòng', 'Vị trí', 'Cơ sở', 'Trạng thái'];
    const rows = rooms.map((r) => [
      r.roomCode,
      `"${r.roomName}"`,
      `"${r.department}"`,
      `"${r.location}"`,
      `"${r.facility}"`,
      r.status === 'ACTIVE'
        ? 'Đang hoạt động'
        : r.status === 'MAINTENANCE'
          ? 'Bảo trì'
          : 'Tạm ngừng',
    ]);
    const csvContent =
      'data:text/csv;charset=utf-8,\uFEFF' +
      [headers.join(','), ...rows.map((row) => row.join(','))].join('\n');
    const encodedUri = encodeURI(csvContent);
    const link = document.createElement('a');
    link.setAttribute('href', encodedUri);
    link.setAttribute(
      'download',
      `danh-sach-buong-kham-${new Date().toISOString().slice(0, 10)}.csv`,
    );
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  return (
    <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3 p-4 rounded-2xl bg-white border border-border shadow-2xs">
      <div className="flex flex-wrap items-center gap-3 flex-1">
        {/* Search Input */}
        <div className="relative min-w-[260px] flex-1 sm:flex-initial">
          <Search
            className="w-4 h-4 text-slate-400 absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none"
            aria-hidden="true"
          />
          <input
            type="text"
            value={filters.search}
            onChange={(e) => onChange({ search: e.target.value })}
            placeholder="Tìm theo mã phòng hoặc tên phòng..."
            className="w-full h-10 pl-10 pr-3 rounded-xl border border-border bg-canvas text-xs text-ink placeholder:text-sub focus:outline-hidden focus:ring-2 focus:ring-primary focus:bg-white transition-all"
          />
        </div>

        {/* Department Select */}
        <select
          value={filters.department}
          onChange={(e) => onChange({ department: e.target.value })}
          aria-label="Lọc theo khoa phòng"
          className="h-10 px-3 rounded-xl border border-border bg-canvas text-xs text-ink focus:outline-hidden focus:ring-2 focus:ring-primary focus:bg-white transition-all"
        >
          <option value="">Tất cả khoa phòng</option>
          {departments.map((dept) => (
            <option key={dept} value={dept}>
              {dept}
            </option>
          ))}
        </select>

        {/* Status Select */}
        <select
          value={filters.status}
          onChange={(e) => onChange({ status: e.target.value })}
          aria-label="Lọc theo trạng thái"
          className="h-10 px-3 rounded-xl border border-border bg-canvas text-xs text-ink focus:outline-hidden focus:ring-2 focus:ring-primary focus:bg-white transition-all"
        >
          <option value="">Tất cả trạng thái</option>
          <option value="ACTIVE">Đang hoạt động</option>
          <option value="MAINTENANCE">Bảo trì</option>
          <option value="INACTIVE">Tạm ngừng</option>
        </select>
      </div>

      {/* Action buttons */}
      <div className="flex items-center gap-2.5 self-end lg:self-center shrink-0">
        <Button variant="secondary" size="sm" onClick={handleExportCsv} className="gap-1.5 h-10">
          <Download className="w-4 h-4" aria-hidden="true" />
          <span>Xuất Excel</span>
        </Button>
        <Button variant="primary" size="sm" onClick={onAddRoom} className="gap-1.5 h-10 shadow-xs">
          <Plus className="w-4 h-4" aria-hidden="true" />
          <span>Thêm buồng khám</span>
        </Button>
      </div>
    </div>
  );
}
