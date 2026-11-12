import { Building2, DoorOpen, FlaskConical, ShieldCheck } from 'lucide-react';

import { Card } from '@/shared/ui/card';

import type { DiagnosticServiceMetrics as DiagnosticServiceMetricsData } from '../types/diagnostic-service.types';

interface DiagnosticServiceMetricsProps {
  metrics: DiagnosticServiceMetricsData;
}

export function DiagnosticServiceMetrics({ metrics }: DiagnosticServiceMetricsProps) {
  const cards = [
    {
      title: 'Tổng dịch vụ CLS',
      value: metrics.total,
      subtitle: `${metrics.active} đang hoạt động · ${metrics.inactive} ngừng`,
      icon: FlaskConical,
      iconStyle: 'bg-sky-50 text-primary',
    },
    {
      title: 'Cần xác nhận kết quả',
      value: metrics.requiresVerificationCount,
      subtitle: 'Yêu cầu duyệt kết quả chuyên môn',
      icon: ShieldCheck,
      iconStyle: 'bg-emerald-50 text-emerald-600',
    },
    {
      title: 'Khoa phòng thực hiện',
      value: metrics.departmentsCount,
      subtitle: 'Khoa cận lâm sàng phụ trách',
      icon: Building2,
      iconStyle: 'bg-amber-50 text-amber-600',
    },
    {
      title: 'Điểm phục vụ hoạt động',
      value: metrics.activeServicePointsCount,
      subtitle: 'Buồng và bàn kỹ thuật đang mở',
      icon: DoorOpen,
      iconStyle: 'bg-sky-50 text-primary',
    },
  ];

  return (
    <section
      aria-label="Thống kê dịch vụ cận lâm sàng"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <Card key={card.title} className="flex items-center justify-between p-5">
            <div className="min-w-0 pr-3">
              <p className="text-xs font-semibold text-sub">{card.title}</p>
              <p className="mt-2 text-2xl font-bold leading-none text-ink">{card.value}</p>
              <p className="mt-2 truncate text-[11px] text-sub">{card.subtitle}</p>
            </div>
            <span
              className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl ${card.iconStyle}`}
            >
              <Icon aria-hidden="true" className="h-5 w-5" />
            </span>
          </Card>
        );
      })}
    </section>
  );
}
