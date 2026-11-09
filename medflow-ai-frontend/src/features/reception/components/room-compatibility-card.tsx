import { CheckCircle2, Users } from 'lucide-react';

import type { AvailableRoomItem, RoomCompatibilityStatus } from '../types/assign-room.types';

const STATUS_META: Record<RoomCompatibilityStatus, { label: string; className: string }> = {
  READY: { label: 'Sẵn sàng', className: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
  BUSY: { label: 'Đang bận', className: 'bg-amber-50 text-amber-700 border-amber-200' },
  OVERLOADED: { label: 'Quá tải', className: 'bg-rose-50 text-rose-700 border-rose-200' },
};

interface RoomCompatibilityCardProps {
  room: AvailableRoomItem;
  selected: boolean;
  onSelect: (roomId: string) => void;
}

export function RoomCompatibilityCard({ room, selected, onSelect }: RoomCompatibilityCardProps) {
  const meta = STATUS_META[room.status];

  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      aria-label={`Chọn ${room.name}`}
      onClick={() => onSelect(room.id)}
      className={`w-full text-left rounded-2xl border p-4 transition-colors bg-white ${
        selected ? 'border-primary ring-2 ring-primary/20' : 'border-border hover:border-primary'
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-sm font-bold text-navy">
            {room.name} ({room.location})
          </div>
          <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
            {room.capacityPerSlot !== undefined && (
              <span>Sức chứa: {room.capacityPerSlot} người/slot</span>
            )}
            <span className="inline-flex items-center gap-1">
              <Users className="w-3.5 h-3.5" aria-hidden="true" />
              Đang chờ: {room.waitingCount}
            </span>
            <span className="inline-flex items-center gap-1">
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" aria-hidden="true" />
              Tương thích: {room.compatibilityPercent}%
            </span>
          </div>
        </div>
        <span
          className={`shrink-0 rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${meta.className}`}
        >
          {meta.label}
        </span>
      </div>
    </button>
  );
}
