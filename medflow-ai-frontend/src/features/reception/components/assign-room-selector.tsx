import type { AvailableRoomItem } from '../types/assign-room.types';
import { RoomCompatibilityCard } from './room-compatibility-card';

interface AssignRoomSelectorProps {
  serviceName: string;
  requirement: string;
  rooms: AvailableRoomItem[];
  selectedRoomId: string | null;
  onSelect: (roomId: string) => void;
}

export function AssignRoomSelector({
  serviceName,
  requirement,
  rooms,
  selectedRoomId,
  onSelect,
}: AssignRoomSelectorProps) {
  return (
    <section className="space-y-4" aria-label="Chọn buồng khám tương thích">
      <div className="rounded-2xl border border-border bg-white p-4">
        <div className="text-sm font-bold text-navy">{serviceName}</div>
        <div className="text-xs text-sub mt-1">{requirement}</div>
      </div>
      <div role="radiogroup" aria-label="Danh sách buồng khám" className="space-y-3">
        {rooms.map((room) => (
          <RoomCompatibilityCard
            key={room.id}
            room={room}
            selected={room.id === selectedRoomId}
            onSelect={onSelect}
          />
        ))}
      </div>
    </section>
  );
}
