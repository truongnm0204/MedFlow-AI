import { CheckCircle2, Clock, FileBarChart2, Users } from 'lucide-react';

import type { OperationalReportItem } from '../types/operational-report.types';

interface ReportMetricsProps {
  reports: OperationalReportItem[];
}

export function ReportMetrics({ reports }: ReportMetricsProps) {
  const reportCount = String(reports.length).padStart(2, '0');

  const cards = [
    {
      title: 'LƯỢT TIẾP NHẬN',
      value: '326',
      unit: 'Lượt',
      subtext: 'Số liệu tổng hợp trong ngày',
      isBadgeSubtext: false,
      icon: Users,
      iconBg: 'bg-sky-50 text-primary border border-sky-100',
    },
    {
      title: 'THỜI GIAN CHỜ TRUNG VỊ',
      value: '18',
      unit: 'Phút',
      subtext: 'Theo điểm phục vụ',
      isBadgeSubtext: true,
      icon: Clock,
      iconBg: 'bg-sky-50 text-primary border border-sky-100',
    },
    {
      title: 'TỶ LỆ HOÀN TẤT',
      value: '94%',
      unit: 'Lượt',
      subtext: 'Không bao gồm ca đang xử lý',
      isBadgeSubtext: false,
      icon: CheckCircle2,
      iconBg: 'bg-emerald-50 text-emerald-600 border border-emerald-100',
    },
    {
      title: 'BÁO CÁO ĐÃ TẠO',
      value: reportCount,
      unit: 'Báo cáo',
      subtext: 'Dữ liệu tổng hợp, ít định danh',
      isBadgeSubtext: false,
      icon: FileBarChart2,
      iconBg: 'bg-sky-50 text-primary border border-sky-100',
    },
  ];

  return (
    <section
      className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
      aria-label="Chỉ số tổng hợp báo cáo vận hành"
    >
      {cards.map((card) => {
        const IconComponent = card.icon;
        return (
          <div
            key={card.title}
            className="flex min-h-32 items-center justify-between rounded-lg border border-border bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
          >
            <div className="min-w-0 pr-3">
              <p className="text-xs font-bold tracking-wide text-sub">{card.title}</p>
              <div className="mt-2 flex items-baseline gap-2">
                <span className="text-3xl font-bold leading-none text-ink">{card.value}</span>
                <span className="text-xs font-semibold text-sub">{card.unit}</span>
              </div>
              <div className="mt-3">
                {card.isBadgeSubtext ? (
                  <span className="inline-flex items-center rounded border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-[11px] font-semibold text-primary">
                    {card.subtext}
                  </span>
                ) : (
                  <p className="truncate text-[11px] text-sub">{card.subtext}</p>
                )}
              </div>
            </div>
            <span
              className={`grid h-12 w-12 shrink-0 place-items-center rounded-lg shadow-xs ${card.iconBg}`}
            >
              <IconComponent className="h-5 w-5" aria-hidden="true" />
            </span>
          </div>
        );
      })}
    </section>
  );
}
