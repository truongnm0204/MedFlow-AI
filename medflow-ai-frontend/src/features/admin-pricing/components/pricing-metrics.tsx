import { AlertCircle, CheckCircle2, Coins, CreditCard } from 'lucide-react';

import type { ServicePricingMetrics } from '../types/service-pricing.types';

interface PricingMetricsProps {
  metrics: ServicePricingMetrics;
}

export function PricingMetrics({ metrics }: PricingMetricsProps) {
  const formatCurrency = (val: number) => `${val.toLocaleString('vi-VN')} đ`;

  const cards = [
    {
      id: 'total',
      title: 'DỊCH VỤ NIÊM YẾT',
      value: String(metrics.totalServices).padStart(2, '0'),
      subtext: 'Danh mục dịch vụ y tế & kỹ thuật',
      subtextColor: 'text-sub',
      icon: CreditCard,
      iconBg: 'bg-sky-50 text-primary',
    },
    {
      id: 'active',
      title: 'ĐANG ÁP DỤNG HIỆU LỰC',
      value: String(metrics.activePricingCount).padStart(2, '0'),
      subtext: '● Biểu phí hiện hành đã phê duyệt',
      subtextColor: 'text-emerald-700 font-medium',
      icon: CheckCircle2,
      iconBg: 'bg-emerald-50 text-emerald-600',
    },
    {
      id: 'suspended',
      title: 'TẠM DỪNG NIÊM YẾT',
      value: String(metrics.suspendedCount).padStart(2, '0'),
      subtext: '● Cần thẩm định hoặc điều chỉnh',
      subtextColor: 'text-amber-700 font-medium',
      icon: AlertCircle,
      iconBg: 'bg-amber-50 text-amber-600',
    },
    {
      id: 'average',
      title: 'MỨC GIÁ TRUNG BÌNH',
      value: formatCurrency(metrics.averagePrice),
      subtext: `Biên độ: ${formatCurrency(metrics.lowestPrice)} – ${formatCurrency(metrics.highestPrice)}`,
      subtextColor: 'text-sub',
      icon: Coins,
      iconBg: 'bg-indigo-50 text-indigo-600',
    },
  ];

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {cards.map((card) => {
        const IconComponent = card.icon;
        return (
          <div
            key={card.id}
            className="flex items-center justify-between rounded-xl border border-border bg-white p-4 shadow-xs transition-shadow hover:shadow-sm"
          >
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wider text-sub">
                {card.title}
              </p>
              <div className="mt-1 text-2xl font-bold tracking-tight text-ink">{card.value}</div>
              <p className={`mt-1 text-[11px] ${card.subtextColor}`}>{card.subtext}</p>
            </div>
            <div
              className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${card.iconBg}`}
            >
              <IconComponent className="h-5 w-5" aria-hidden="true" />
            </div>
          </div>
        );
      })}
    </div>
  );
}
