import { useMemo, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { RoomFilters } from '../components/room-filters';
import { RoomMetrics } from '../components/room-metrics';
import { RoomTable } from '../components/room-table';
import { ROOM_METRICS_MOCK, ROOMS_MOCK_DATA } from '../data/room-mock-data';
import type { RoomFilters as RoomFiltersType, RoomItem } from '../types/room.types';

export function RoomListPage() {
  const [filters, setFilters] = useState<RoomFiltersType>({
    search: '',
    department: '',
    status: '',
  });

  const departments = useMemo(() => {
    return Array.from(new Set(ROOMS_MOCK_DATA.map((r) => r.department)));
  }, []);

  const filteredRooms = useMemo(() => {
    return ROOMS_MOCK_DATA.filter((room) => {
      const q = filters.search.trim().toLowerCase();
      const matchesSearch =
        !q ||
        room.roomCode.toLowerCase().includes(q) ||
        room.roomName.toLowerCase().includes(q) ||
        room.location.toLowerCase().includes(q);

      if (!matchesSearch) return false;

      if (filters.department && room.department !== filters.department) {
        return false;
      }

      if (filters.status && room.status !== filters.status) {
        return false;
      }

      return true;
    });
  }, [filters]);

  const handleFilterChange = (partial: Partial<RoomFiltersType>) => {
    setFilters((prev) => ({ ...prev, ...partial }));
  };

  const handleViewRoom = (room: RoomItem) => {
    alert(`Xem chi tiết buồng khám: ${room.roomName} (${room.roomCode})`);
  };

  const handleAddRoom = () => {
    alert('Chức năng thêm mới buồng khám (SCR-ADM-ROOM-02) sẽ kết nối tại đây.');
  };

  return (
    <AdminPortalShell
      activeTab="Buồng khám & Kỹ thuật"
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Danh mục Buồng khám & Kỹ thuật (SCR-ADM-ROOM-01)' },
      ]}
    >
      <div className="p-6 space-y-6 max-w-[1720px] mx-auto">
        {/* Page Title */}
        <div>
          <h1 className="text-xl font-bold text-ink tracking-tight">
            Danh mục Buồng khám & Kỹ thuật
          </h1>
          <p className="text-xs text-sub mt-1">
            Quản lý danh sách các buồng khám chuyên khoa, buồng cận lâm sàng và trạng thái khả dụng
            đón tiếp bệnh nhân.
          </p>
        </div>

        {/* 4 Stat Cards */}
        <RoomMetrics metrics={ROOM_METRICS_MOCK} />

        {/* Filters and Search Bar */}
        <RoomFilters
          filters={filters}
          onChange={handleFilterChange}
          departments={departments}
          rooms={filteredRooms}
          onAddRoom={handleAddRoom}
        />

        {/* Rooms Data Table */}
        <RoomTable rooms={filteredRooms} onViewRoom={handleViewRoom} />
      </div>
    </AdminPortalShell>
  );
}
