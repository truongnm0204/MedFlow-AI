import { Activity, AlertTriangle, Building2, DoorOpen } from 'lucide-react';

import type { RoomMetrics as RoomMetricsType } from '../types/room.types';

interface RoomMetricsProps {
  metrics: RoomMetricsType;
}

export function RoomMetrics({ metrics }: RoomMetricsProps) {
  const cards = [
    {
      title: 'TỔNG SỐ BUỒNG KHÁM',
      value: `${metrics.totalRooms} Phòng`,
      desc: '20 phòng đang hoạt động',
      icon: DoorOpen,
      tone: 'text-primary',
    },
    {
      title: 'ĐANG HOẠT ĐỘNG',
      value: `${metrics.activeRooms} Phòng`,
      desc: 'Tiếp nhận khám trực tiếp',
      icon: Activity,
      tone: 'text-emerald-600',
    },
    {
      title: 'TẠM NGỪNG / BẢO TRÌ',
      value: `${metrics.maintenanceRooms < 10 ? `0${metrics.maintenanceRooms}` : metrics.maintenanceRooms} Phòng`,
      desc: 'Không dùng vào lịch mới',
      icon: AlertTriangle,
      tone: 'text-amber-500',
    },
    {
      title: 'KHOA PHÒNG LIÊN KẾT',
      value: `${metrics.linkedDepartments} Khoa phòng`,
      desc: 'Theo danh mục cơ sở',
      icon: Building2,
      tone: 'text-sky-600',
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <div
            key={card.title}
            className="p-5 rounded-2xl bg-white border border-border shadow-2xs space-y-1 transition-all hover:border-slate-300"
          >
            <div className="flex items-center justify-between text-xs font-semibold text-sub tracking-wider">
              <span>{card.title}</span>
              <Icon className={`w-4 h-4 ${card.tone}`} aria-hidden="true" />
            </div>
            <div className="text-2xl font-black text-ink">{card.value}</div>
            <div className="text-[11px] text-sub">{card.desc}</div>
          </div>
        );
      })}
    </div>
  );
}
