import { Clock, DoorOpen } from 'lucide-react';
import type { DoctorWorkloadItem, WorkloadLevel } from '../types/assign-doctor.types';

const LEVEL_META: Record<WorkloadLevel, { label: string; badge: string }> = {
  LOW: { label: 'Tải thấp', badge: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
  MEDIUM: { label: 'Tải vừa', badge: 'bg-amber-50 text-amber-700 border-amber-200' },
  HIGH: { label: 'Tải cao', badge: 'bg-rose-50 text-rose-700 border-rose-200' },
};

interface DoctorWorkloadCardProps {
  doctor: DoctorWorkloadItem;
  selected: boolean;
  onSelect: (doctorId: string) => void;
}

export function DoctorWorkloadCard({ doctor, selected, onSelect }: DoctorWorkloadCardProps) {
  const meta = LEVEL_META[doctor.workloadLevel];

  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      aria-label={`Chọn ${doctor.fullName}`}
      onClick={() => onSelect(doctor.id)}
      className={`w-full text-left rounded-2xl border bg-white p-5 transition-all flex items-start gap-4 ${
        selected
          ? 'border-primary ring-2 ring-primary shadow-sm'
          : 'border-border hover:border-primary/50'
      }`}
    >
      <span
        aria-hidden="true"
        className={`mt-1 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 ${
          selected ? 'border-primary' : 'border-border'
        }`}
      >
        {selected && <span className="h-2.5 w-2.5 rounded-full bg-primary" />}
      </span>

      <span className="flex-1 min-w-0 space-y-2">
        <span className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-bold text-navy">{doctor.fullName}</span>
          <span className={`rounded-full border px-2.5 py-0.5 text-xs font-semibold ${meta.badge}`}>
            {meta.label} (Đang chờ {doctor.waitingCount})
          </span>
        </span>
        <span className="flex items-center gap-1.5 text-sm text-sub">
          <DoorOpen className="h-4 w-4" aria-hidden="true" />
          {doctor.roomName} ({doctor.roomLocation})
        </span>
        <span className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-sub">
          <span>
            STT hiện tại: <strong className="text-ink">{doctor.currentTicket}</strong>
          </span>
          {doctor.estimatedWait && (
            <span className="flex items-center gap-1">
              <Clock className="h-4 w-4" aria-hidden="true" />
              Dự kiến: {doctor.estimatedWait}
            </span>
          )}
        </span>
      </span>
    </button>
  );
}
