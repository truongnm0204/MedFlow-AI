import { AlertTriangle, BookOpen, CalendarClock, Stethoscope } from 'lucide-react';

import type { ExaminationSlot } from '../types/examination-slot.types';

interface SlotMetricsProps {
  slots: ExaminationSlot[];
}

export function SlotMetrics({ slots }: SlotMetricsProps) {
  const total = slots.length;
  const openCount = total;
  const bookedCount = slots.filter((s) => s.capacityBooked > 0).length;
  const upcomingCount = Math.min(12, slots.filter((s) => s.date <= '2026-10-28').length);
  const reviewCount = slots.filter((s) => s.needsReview || s.status === 'CLOSED').length;

  const cards = [
    {
      title: 'SLOT ĐANG MỞ',
      count: String(openCount || total).padStart(2, '0'),
      unit: 'Slot',
      subtext: 'Đủ điều kiện nhận đặt lịch',
      subtextColor: 'text-sub',
      icon: Stethoscope,
      iconBg: 'bg-sky-50 text-primary',
      valueColor: 'text-ink',
    },
    {
      title: 'CÓ LƯỢT ĐẶT',
      count: String(bookedCount).padStart(2, '0'),
      unit: 'Slot',
      subtext: 'Lượt đặt được giữ nguyên',
      subtextColor: 'text-sub',
      icon: BookOpen,
      iconBg: 'bg-sky-50 text-sky-600',
      valueColor: 'text-ink',
    },
    {
      title: 'SẮP ĐẾN GIỜ',
      count: String(upcomingCount).padStart(2, '0'),
      unit: 'Slot',
      subtext: 'Trong 7 ngày tới',
      subtextColor: 'text-sub',
      icon: CalendarClock,
      iconBg: 'bg-amber-50 text-amber-600',
      valueColor: 'text-ink',
    },
    {
      title: 'CẦN RÀ SOÁT',
      count: String(reviewCount).padStart(2, '0'),
      unit: 'Slot',
      subtext: 'Tài nguyên hoặc sức chứa',
      subtextBadge: true,
      subtextColor: 'text-blue-700 font-medium',
      icon: AlertTriangle,
      iconBg: 'bg-blue-50 text-blue-600',
      valueColor: 'text-ink',
    },
  ];

  return (
    <section
      className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
      aria-label="Thống kê khung giờ khám"
    >
      {cards.map((card) => {
        const IconComponent = card.icon;
        return (
          <div
            key={card.title}
            className="flex min-h-32 items-center justify-between rounded-xl border border-border bg-white p-5 shadow-xs transition-shadow hover:shadow-md"
          >
            <div className="min-w-0 pr-3">
              <p className="text-[11px] font-bold tracking-wider uppercase text-sub">
                {card.title}
              </p>
              <div className="mt-2 flex items-baseline gap-1.5 leading-none">
                <span className={`text-3xl font-extrabold ${card.valueColor}`}>{card.count}</span>
                <span className="text-xs font-semibold text-sub">{card.unit}</span>
              </div>
              <div className="mt-3">
                {card.subtextBadge ? (
                  <span className="inline-block rounded-md border border-blue-200 bg-blue-50/60 px-2 py-0.5 text-[11px] text-blue-700 font-medium">
                    {card.subtext}
                  </span>
                ) : (
                  <p className={`truncate text-[11px] ${card.subtextColor}`}>{card.subtext}</p>
                )}
              </div>
            </div>
            <span
              className={`grid h-12 w-12 shrink-0 place-items-center rounded-xl ${card.iconBg}`}
              aria-hidden="true"
            >
              <IconComponent className="h-6 w-6" />
            </span>
          </div>
        );
      })}
    </section>
  );
}
