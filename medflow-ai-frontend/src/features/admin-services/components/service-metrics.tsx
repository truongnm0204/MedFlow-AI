import { CreditCard, Globe, Hospital, PauseCircle } from 'lucide-react';

import type { ExaminationService } from '../types/examination-service.types';

interface ServiceMetricsProps {
  services: ExaminationService[];
}

export function ServiceMetrics({ services }: ServiceMetricsProps) {
  const total = services.length;
  const onlineCount = services.filter((s) => s.onlineBooking).length;
  const onlinePercent = total > 0 ? ((onlineCount / total) * 100).toFixed(1) : '0';

  const consultationCount = services.filter(
    (s) =>
      s.category === 'Hội chẩn Lâm sàng' ||
      s.name.includes('Tổng quát') ||
      s.name.includes('Cấp cứu'),
  ).length;
  const specialistCount = Math.max(0, total - consultationCount);
  const suspendedCount = services.filter((s) => s.status === 'SUSPENDED').length;

  const cards = [
    {
      title: 'TỔNG SỐ DỊCH VỤ KHÁM',
      value: String(total).padStart(2, '0'),
      subtext: `• ${specialistCount} Chuyên khoa • ${consultationCount} Tổng quát & hội chẩn`,
      icon: Hospital,
      iconBg: 'bg-sky-50 text-primary',
      valueColor: 'text-ink',
    },
    {
      title: 'MỞ ĐẶT HẸN ONLINE',
      value: String(onlineCount).padStart(2, '0'),
      subtext: `● ${onlinePercent}% Sẵn sàng trên Cổng BN`,
      subtextColor: 'text-primary font-medium',
      icon: Globe,
      iconBg: 'bg-sky-50 text-primary',
      valueColor: 'text-ink',
    },
    {
      title: 'GIÁ HIỆU LỰC TẠI CƠ SỞ',
      value: 'Theo cơ sở',
      subtext: '● Chỉ xem - Quản lý tại UC52',
      subtextColor: 'text-primary font-medium',
      icon: CreditCard,
      iconBg: 'bg-sky-50 text-primary',
      valueColor: 'text-ink text-2xl',
    },
    {
      title: 'TẠM NGỪNG DỊCH VỤ',
      value: String(suspendedCount).padStart(2, '0'),
      subtext: '⟳ Theo trạng thái danh mục',
      icon: PauseCircle,
      iconBg: 'bg-amber-50 text-amber-600',
      valueColor: 'text-amber-600',
    },
  ];

  return (
    <section
      className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
      aria-label="Thống kê dịch vụ khám"
    >
      {cards.map((card) => {
        const IconComponent = card.icon;
        return (
          <div
            key={card.title}
            className="flex min-h-32 items-center justify-between rounded-lg border border-border bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
          >
            <div className="min-w-0 pr-3">
              <p className="text-[11px] font-semibold tracking-wide text-sub">{card.title}</p>
              <p
                className={`mt-2 font-bold leading-none ${card.valueColor} ${card.value.length > 2 ? 'text-2xl' : 'text-3xl'}`}
              >
                {card.value}
              </p>
              <p className={`mt-3 truncate text-[11px] ${card.subtextColor ?? 'text-sub'}`}>
                {card.subtext}
              </p>
            </div>
            <span
              className={`grid h-12 w-12 shrink-0 place-items-center rounded-lg ${card.iconBg}`}
            >
              <IconComponent className="h-6 w-6" />
            </span>
          </div>
        );
      })}
    </section>
  );
}
