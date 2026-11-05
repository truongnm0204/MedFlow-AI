import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { CheckCircle2 } from 'lucide-react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { RoomDeactivateModal } from '../components/room-deactivate-modal';
import { RoomFilters } from '../components/room-filters';
import { RoomMetrics } from '../components/room-metrics';
import { RoomTable } from '../components/room-table';
import { ROOM_METRICS_MOCK, ROOMS_MOCK_DATA } from '../data/room-mock-data';
import type {
  RoomFilters as RoomFiltersType,
  RoomItem,
  RoomMetrics as RoomMetricsType,
} from '../types/room.types';

export function RoomListPage() {
  const navigate = useNavigate();
  const [rooms, setRooms] = useState<RoomItem[]>(ROOMS_MOCK_DATA);
  const [filters, setFilters] = useState<RoomFiltersType>({
    search: '',
    department: '',
    status: '',
  });

  const [deactivatingRoom, setDeactivatingRoom] = useState<RoomItem | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);

  const departments = useMemo(() => {
    return Array.from(new Set(rooms.map((r) => r.department)));
  }, [rooms]);

  const filteredRooms = useMemo(() => {
    return rooms.filter((room) => {
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
  }, [filters, rooms]);

  const metrics: RoomMetricsType = useMemo(() => {
    const activeCount = rooms.filter((r) => r.status === 'ACTIVE').length;
    const maintenanceCount = rooms.filter((r) => r.status !== 'ACTIVE').length;
    const initialActive = ROOMS_MOCK_DATA.filter((r) => r.status === 'ACTIVE').length;
    const initialMaint = ROOMS_MOCK_DATA.filter((r) => r.status !== 'ACTIVE').length;
    const activeDelta = activeCount - initialActive;
    const maintDelta = maintenanceCount - initialMaint;
    return {
      totalRooms: ROOM_METRICS_MOCK.totalRooms,
      activeRooms: ROOM_METRICS_MOCK.activeRooms + activeDelta,
      maintenanceRooms: ROOM_METRICS_MOCK.maintenanceRooms + maintDelta,
      linkedDepartments: ROOM_METRICS_MOCK.linkedDepartments,
    };
  }, [rooms]);

  const handleFilterChange = (partial: Partial<RoomFiltersType>) => {
    setFilters((prev) => ({ ...prev, ...partial }));
  };

  const handleViewRoom = (room: RoomItem) => {
    navigate(`/admin/rooms/${room.id}/edit`);
  };

  const handleAddRoom = () => {
    navigate('/admin/rooms/new');
  };

  const handleConfirmDeactivate = (roomId: string) => {
    const room = rooms.find((item) => item.id === roomId);
    if (room) {
      setRooms((prev) =>
        prev.map((item) => (item.id === roomId ? { ...item, status: 'INACTIVE' } : item)),
      );
      setFeedbackMessage(`Đã chuyển trạng thái buồng khám ${room.roomName} sang Ngừng hoạt động.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    }
    setDeactivatingRoom(null);
  };

  const handleToggleStatus = (id: string) => {
    const room = rooms.find((item) => item.id === id);
    if (!room) return;
    if (room.status === 'ACTIVE' || room.status === 'MAINTENANCE') {
      setDeactivatingRoom(room);
    } else {
      setRooms((prev) =>
        prev.map((item) => (item.id === id ? { ...item, status: 'ACTIVE' } : item)),
      );
      setFeedbackMessage(`Đã kích hoạt lại hoạt động cho buồng khám ${room.roomName}.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    }
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
        <RoomMetrics metrics={metrics} />

        {/* Filters and Search Bar */}
        <RoomFilters
          filters={filters}
          onChange={handleFilterChange}
          departments={departments}
          rooms={filteredRooms}
          onAddRoom={handleAddRoom}
        />

        {/* Rooms Data Table */}
        <RoomTable
          rooms={filteredRooms}
          onViewRoom={handleViewRoom}
          onRequestDeactivate={(room) => setDeactivatingRoom(room)}
          onToggleStatus={handleToggleStatus}
        />
      </div>

      {/* Deactivate Room Modal */}
      <RoomDeactivateModal
        room={deactivatingRoom}
        open={Boolean(deactivatingRoom)}
        onClose={() => setDeactivatingRoom(null)}
        onConfirm={handleConfirmDeactivate}
      />

      {/* Feedback Toast */}
      {feedbackMessage && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-semibold text-emerald-800 shadow-lg"
        >
          <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
          <span>{feedbackMessage}</span>
        </div>
      )}
    </AdminPortalShell>
  );
}
