import { Building2, CalendarClock, DoorOpen, Stethoscope } from 'lucide-react';

import type { HospitalAdminStats } from '../types/hospital-admin.types';

interface HospitalAdminStatCardsProps {
  stats: HospitalAdminStats;
}

export function HospitalAdminStatCards({ stats }: HospitalAdminStatCardsProps) {
  const inactiveDepartments = Math.max(0, stats.totalDepartments - stats.activeDepartments);
  const maintenanceRooms = Math.max(0, stats.totalRooms - stats.activeRooms);

  const cards = [
    {
      key: 'departments',
      label: 'Khoa phòng hoạt động',
      value: `${String(stats.activeDepartments).padStart(2, '0')}/${String(stats.totalDepartments).padStart(2, '0')}`,
      hint: `${inactiveDepartments} khoa tạm ngừng • Cơ sở Hà Nội`,
      tone: 'border-sky-200 bg-sky-50 text-sky-700',
      icon: Building2,
    },
    {
      key: 'rooms',
      label: 'Buồng khám sẵn sàng',
      value: `${String(stats.activeRooms).padStart(2, '0')}/${String(stats.totalRooms).padStart(2, '0')}`,
      hint: `${maintenanceRooms} phòng bảo trì • đón tiếp ngoại trú`,
      tone: 'border-emerald-200 bg-emerald-50 text-emerald-700',
      icon: DoorOpen,
    },
    {
      key: 'doctors-services',
      label: 'Bác sĩ & Dịch vụ mở đặt hẹn',
      value: `${String(stats.activeDoctors).padStart(2, '0')} BS • ${String(stats.onlineServicesCount).padStart(2, '0')} DV`,
      hint: 'Đã đồng bộ danh mục đặt khám trực tuyến',
      tone: 'border-amber-200 bg-amber-50 text-amber-700',
      icon: Stethoscope,
    },
    {
      key: 'open-slots',
      label: 'Slot khám đang mở hôm nay',
      value: String(stats.openSlotsToday).padStart(2, '0'),
      hint: `${String(stats.reviewSlotsCount).padStart(2, '0')} khung giờ cần rà soát sức chứa`,
      tone: 'border-border bg-white text-ink',
      icon: CalendarClock,
    },
  ];

  return (
    <section
      aria-label="Chỉ số tổng quan cấu hình bệnh viện"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <div
            key={card.key}
            className={`flex items-start justify-between gap-3 rounded-2xl border p-4 shadow-2xs ${card.tone}`}
          >
            <div className="min-w-0 space-y-1">
              <div className="font-mono text-2xl font-bold whitespace-nowrap tabular-nums">
                {card.value}
              </div>
              <div className="text-xs font-bold">{card.label}</div>
              <div className="text-[11px] opacity-80">{card.hint}</div>
            </div>
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-current/15 bg-white/80 shadow-2xs">
              <Icon aria-hidden="true" className="h-5 w-5" />
            </span>
          </div>
        );
      })}
    </section>
  );
}
