import { useMemo, useRef, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { RoomFilters } from '../components/room-filters';
import { RoomMetrics } from '../components/room-metrics';
import { RoomTable } from '../components/room-table';
import { INITIAL_ROOMS } from '../data/room-mock-data';
import { ROOM_STATUS_LABELS } from '../types/room.types';
import type { RoomFilters as RoomFiltersState, RoomItem } from '../types/room.types';

const PAGE_SIZE = 10;

function exportRoomsToCsv(rooms: RoomItem[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = ['Mã phòng', 'Tên buồng khám', 'Khoa phòng', 'Vị trí / Tầng', 'Trạng thái'];
  const rows = rooms.map((r) => [
    r.code,
    r.name,
    r.department,
    r.location,
    ROOM_STATUS_LABELS[r.status],
  ]);
  const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-muc-buong-kham-medflow.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-ROOM-01 · Danh mục Buồng khám & Kỹ thuật. */
export function RoomListPage() {
  const searchRef = useRef<HTMLInputElement>(null);
  const [rooms] = useState<RoomItem[]>(INITIAL_ROOMS);
  const [filters, setFilters] = useState<RoomFiltersState>({
    searchQuery: '',
    department: '',
    status: '',
  });
  const [page, setPage] = useState(1);

  const departments = useMemo(
    () =>
      Array.from(new Set(rooms.map((r) => r.department))).sort((a, b) => a.localeCompare(b, 'vi')),
    [rooms],
  );

  const metrics = useMemo(() => {
    const active = rooms.filter((r) => r.status === 'ACTIVE').length;
    return {
      total: rooms.length,
      active,
      inactive: rooms.length - active,
      departments: departments.length,
    };
  }, [rooms, departments]);

  const filtered = useMemo(() => {
    const term = filters.searchQuery.trim().toLowerCase();
    return rooms.filter(
      (r) =>
        (!term || r.code.toLowerCase().includes(term) || r.name.toLowerCase().includes(term)) &&
        (!filters.department || r.department === filters.department) &&
        (!filters.status || r.status === filters.status),
    );
  }, [rooms, filters]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const pageRooms = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  return (
    <AdminPortalShell
      showTabs={true}
      activeTab="Buồng khám & Kỹ thuật"
      breadcrumbText="Danh mục Buồng khám & Kỹ thuật (SCR-ADM-ROOM-01)"
      breadcrumbLink="/admin/rooms"
    >
      <main className="mx-auto max-w-[1720px] space-y-5 px-6 py-6">
        <RoomMetrics metrics={metrics} />
        <RoomFilters
          filters={filters}
          departments={departments}
          searchRef={searchRef}
          onChange={(next) => {
            setFilters(next);
            setPage(1);
          }}
          onExport={() => exportRoomsToCsv(filtered)}
          onAdd={() => undefined}
        />
        <RoomTable
          rooms={pageRooms}
          totalCount={filtered.length}
          currentPage={currentPage}
          pageSize={PAGE_SIZE}
          totalPages={totalPages}
          onPageChange={setPage}
        />
      </main>
    </AdminPortalShell>
  );
}
