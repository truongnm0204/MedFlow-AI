import { Activity, FileSearch, KeyRound, ShieldAlert } from 'lucide-react';

import { Card, CardContent } from '@/shared/ui/card';

import type { AuditLogMetricsDto } from '../types/audit-log.types';

interface AuditLogMetricsCardsProps {
  metrics: AuditLogMetricsDto;
}

export function AuditLogMetricsCards({ metrics }: AuditLogMetricsCardsProps) {
  const cards = [
    {
      label: 'Tổng bản ghi trong phạm vi',
      value: metrics.totalLogs,
      subtitle: 'Nhật ký kiểm toán được phép tra cứu',
      icon: FileSearch,
      toneClass: 'border-sky-200 bg-sky-50 text-primary',
    },
    {
      label: 'Sự kiện trong 24 giờ qua',
      value: metrics.last24HoursCount,
      subtitle: 'Theo đồng hồ bệnh viện (UTC+7)',
      icon: Activity,
      toneClass: 'border-emerald-200 bg-emerald-50 text-emerald-700',
    },
    {
      label: 'Xác thực & Phiên (AUTH)',
      value: metrics.authEventsCount,
      subtitle: 'Đăng nhập, OTP, làm mới & thu hồi phiên',
      icon: KeyRound,
      toneClass: 'border-amber-200 bg-amber-50 text-amber-700',
    },
    {
      label: 'Phân quyền & Cấu hình',
      value: metrics.configAndRbacEventsCount,
      subtitle: 'Thay đổi RBAC, chính sách & tích hợp',
      icon: ShieldAlert,
      toneClass: 'border-rose-200 bg-rose-50 text-rose-700',
    },
  ];

  return (
    <section
      aria-label="Thống kê nhật ký kiểm toán"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <Card key={card.label}>
            <CardContent className="flex items-start justify-between gap-3 p-5">
              <div className="space-y-1">
                <p className="text-xs font-semibold text-sub">{card.label}</p>
                <p className="font-mono text-2xl font-bold tracking-tight text-ink">
                  {card.value.toLocaleString('vi-VN')}
                </p>
                <p className="text-[11px] text-sub">{card.subtitle}</p>
              </div>
              <div
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border ${card.toneClass}`}
              >
                <Icon className="h-5 w-5" aria-hidden="true" />
              </div>
            </CardContent>
          </Card>
        );
      })}
    </section>
  );
}
