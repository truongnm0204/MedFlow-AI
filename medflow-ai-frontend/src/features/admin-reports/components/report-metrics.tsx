import { CalendarX2, CheckCircle2, Clock, Users } from 'lucide-react';

import type { OperationalReportSummary } from '../api/operational-report.api';

interface ReportMetricsProps {
  summary: OperationalReportSummary;
  periodLabel: string;
}

export function ReportMetrics({ summary, periodLabel }: ReportMetricsProps) {
  const hasMedianWait = summary.medianWaitMinutes != null;

  const cards = [
    {
      title: 'LƯỢT TIẾP NHẬN',
      value: String(summary.totalVisits),
      unit: 'Lượt',
      subtext: `Kỳ ${periodLabel} • ${summary.walkInVisits} vãng lai`,
      isBadgeSubtext: false,
      icon: Users,
      iconBg: 'bg-sky-50 text-primary border border-sky-100',
    },
    {
      title: 'THỜI GIAN CHỜ TRUNG VỊ',
      value: hasMedianWait ? String(summary.medianWaitMinutes) : '—',
      unit: hasMedianWait ? 'Phút' : '',
      subtext:
        summary.averageWaitMinutes != null
          ? `Trung bình ${summary.averageWaitMinutes} phút`
          : 'Chưa đủ dữ liệu hàng đợi',
      isBadgeSubtext: true,
      icon: Clock,
      iconBg: 'bg-sky-50 text-primary border border-sky-100',
    },
    {
      title: 'TỶ LỆ HOÀN TẤT',
      value: `${summary.completionRatePercent}%`,
      unit: `${summary.completedVisits}/${summary.totalVisits} ca`,
      subtext: 'Tính trên tổng lượt tiếp nhận',
      isBadgeSubtext: false,
      icon: CheckCircle2,
      iconBg: 'bg-emerald-50 text-emerald-600 border border-emerald-100',
    },
    {
      title: 'LỊCH HẸN & VẮNG MẶT',
      value: String(summary.totalAppointments),
      unit: 'Lịch hẹn',
      subtext: `Vắng mặt (No-show): ${summary.noShowAppointments} ca`,
      isBadgeSubtext: false,
      icon: CalendarX2,
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
                {card.unit && <span className="text-xs font-semibold text-sub">{card.unit}</span>}
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
