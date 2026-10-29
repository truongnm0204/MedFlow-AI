import { Building2, DoorOpen, PauseCircle, Stethoscope } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';

import type { RoomMetrics as RoomMetricsData } from '../types/room.types';

interface RoomMetricsProps {
  metrics: RoomMetricsData;
}

interface Card {
  label: string;
  value: string;
  unit: string;
  hint: string;
  icon: LucideIcon;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function RoomMetrics({ metrics }: RoomMetricsProps) {
  const cards: Card[] = [
    {
      label: 'TỔNG SỐ BUỒNG KHÁM',
      value: String(metrics.total),
      unit: 'Phòng',
      hint: `${metrics.active} phòng đang hoạt động`,
      icon: DoorOpen,
    },
    {
      label: 'ĐANG HOẠT ĐỘNG',
      value: String(metrics.active),
      unit: 'Phòng',
      hint: 'Tiếp nhận khám trực tiếp',
      icon: Stethoscope,
    },
    {
      label: 'TẠM NGỪNG / BẢO TRÌ',
      value: pad(metrics.inactive),
      unit: 'Phòng',
      hint: 'Không dùng vào lịch mới',
      icon: PauseCircle,
    },
    {
      label: 'KHOA PHÒNG LIÊN KẾT',
      value: String(metrics.departments),
      unit: 'Khoa phòng',
      hint: 'Đã liên kết buồng khám',
      icon: Building2,
    },
  ];

  return (
    <section
      aria-label="Chỉ số buồng khám"
      className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4"
    >
      {cards.map(({ label, value, unit, hint, icon: Icon }) => (
        <div key={label} className="rounded-xl border border-border bg-white p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-sub">
              {label}
            </span>
            <Icon className="h-4 w-4 text-primary" aria-hidden="true" />
          </div>
          <div className="mt-2 flex items-baseline gap-1.5">
            <span className="text-2xl font-bold text-navy">{value}</span>
            <span className="text-xs font-semibold text-sub">{unit}</span>
          </div>
          <div className="mt-1 text-[11px] text-sub">{hint}</div>
        </div>
      ))}
    </section>
  );
}
