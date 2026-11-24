import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { CheckCircle2, Loader2 } from 'lucide-react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { RoomDeactivateModal } from '../components/room-deactivate-modal';
import { RoomFilters } from '../components/room-filters';
import { RoomMetrics } from '../components/room-metrics';
import { RoomTable } from '../components/room-table';
import { useAdminRooms, useUpdateRoomMutation } from '../hooks/use-rooms';
import type {
  RoomFilters as RoomFiltersType,
  RoomItem,
  RoomMetrics as RoomMetricsType,
} from '../types/room.types';

const EMPTY_METRICS: RoomMetricsType = {
  totalRooms: 0,
  activeRooms: 0,
  maintenanceRooms: 0,
  linkedDepartments: 0,
};

export function RoomListPage() {
  const navigate = useNavigate();
  const { data, isLoading, isError, error, refetch } = useAdminRooms();
  const updateRoomMutation = useUpdateRoomMutation();

  const rooms = data?.rooms ?? [];
  const metrics = data?.metrics ?? EMPTY_METRICS;

  const [filters, setFilters] = useState<RoomFiltersType>({
    search: '',
    department: '',
    status: '',
  });

  const [deactivatingRoom, setDeactivatingRoom] = useState<RoomItem | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const departments = useMemo(() => {
    return Array.from(new Set(rooms.map((r) => r.department).filter(Boolean)));
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

  const handleFilterChange = (partial: Partial<RoomFiltersType>) => {
    setFilters((prev) => ({ ...prev, ...partial }));
  };

  const handleViewRoom = (room: RoomItem) => {
    navigate(`/admin/rooms/${room.id}/edit`);
  };

  const handleAddRoom = () => {
    navigate('/admin/rooms/new');
  };

  const handleConfirmDeactivate = async (roomId: string) => {
    const room = rooms.find((item) => item.id === roomId);
    if (!room) {
      setDeactivatingRoom(null);
      return;
    }

    setActionError(null);
    try {
      await updateRoomMutation.mutateAsync({
        id: room.id,
        payload: {
          roomCode: room.roomCode,
          name: room.roomName,
          department: room.department,
          location: room.location,
          capacity: room.capacityPerSlot,
          status: 'INACTIVE',
        },
      });
      setFeedbackMessage(`Đã chuyển trạng thái buồng khám ${room.roomName} sang Ngừng hoạt động.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
      setDeactivatingRoom(null);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể ngừng hoạt động buồng khám.');
      setDeactivatingRoom(null);
    }
  };

  const handleToggleStatus = async (id: string) => {
    const room = rooms.find((item) => item.id === id);
    if (!room) return;
    if (room.status === 'ACTIVE' || room.status === 'MAINTENANCE') {
      setDeactivatingRoom(room);
      return;
    }

    setActionError(null);
    try {
      await updateRoomMutation.mutateAsync({
        id: room.id,
        payload: {
          roomCode: room.roomCode,
          name: room.roomName,
          department: room.department,
          location: room.location,
          capacity: room.capacityPerSlot,
          status: 'ACTIVE',
        },
      });
      setFeedbackMessage(`Đã kích hoạt lại hoạt động cho buồng khám ${room.roomName}.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể kích hoạt lại buồng khám.');
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
        <div>
          <h1 className="text-xl font-bold text-ink tracking-tight">
            Danh mục Buồng khám & Kỹ thuật
          </h1>
          <p className="text-xs text-sub mt-1">
            Quản lý danh sách các buồng khám chuyên khoa, buồng cận lâm sàng và trạng thái khả dụng
            đón tiếp bệnh nhân.
          </p>
        </div>

        {isError ? (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                <b>Không thể tải danh mục buồng khám:</b>{' '}
                {error instanceof Error ? error.message : 'Đã xảy ra lỗi kết nối máy chủ.'}
              </span>
              <Button type="button" size="sm" variant="secondary" onClick={() => void refetch()}>
                Thử lại
              </Button>
            </div>
          </Alert>
        ) : null}

        {actionError ? <Alert tone="error">{actionError}</Alert> : null}

        <RoomMetrics metrics={metrics} />

        <RoomFilters
          filters={filters}
          onChange={handleFilterChange}
          departments={departments}
          rooms={filteredRooms}
          onAddRoom={handleAddRoom}
        />

        {isLoading ? (
          <div className="flex items-center justify-center gap-2 rounded-xl border border-border bg-white py-12 text-sm text-sub">
            <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
            <span>Đang tải danh sách buồng khám...</span>
          </div>
        ) : (
          <RoomTable
            rooms={filteredRooms}
            onViewRoom={handleViewRoom}
            onRequestDeactivate={(room) => setDeactivatingRoom(room)}
            onToggleStatus={(id) => void handleToggleStatus(id)}
          />
        )}
      </div>

      <RoomDeactivateModal
        room={deactivatingRoom}
        open={Boolean(deactivatingRoom)}
        onClose={() => setDeactivatingRoom(null)}
        onConfirm={(roomId) => void handleConfirmDeactivate(roomId)}
      />

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
