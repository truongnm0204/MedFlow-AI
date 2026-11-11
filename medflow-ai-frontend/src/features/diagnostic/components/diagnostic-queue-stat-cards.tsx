import { Activity, AlertTriangle, CheckCircle2, Users } from 'lucide-react';

import type { DiagnosticQueueSummaryStatsDto } from '../types/diagnostic.types';

interface DiagnosticQueueStatCardsProps {
  stats: DiagnosticQueueSummaryStatsDto;
}

export function DiagnosticQueueStatCards({ stats }: DiagnosticQueueStatCardsProps) {
  const waitingAndReady = stats.waitingCount + stats.readyCount;
  const calledAndInProgress = stats.calledCount + stats.inProgressCount;

  const cards = [
    {
      id: 'waiting-ready',
      label: 'Đang chờ & Sẵn sàng',
      value: waitingAndReady,
      hint: `${stats.readyCount} sẵn sàng gọi • ${stats.waitingCount} chờ tiếp nhận`,
      tone: 'border-sky-200 bg-sky-50/80 text-sky-800',
      icon: Users,
    },
    {
      id: 'called-in-progress',
      label: 'Đã gọi & Đang thực hiện',
      value: calledAndInProgress,
      hint: `${stats.calledCount} đã gọi • ${stats.inProgressCount} đang thực hiện (${stats.busyServicePointsCount}/${stats.activeServicePointsCount} phòng bận)`,
      tone: 'border-amber-200 bg-amber-50/80 text-amber-800',
      icon: Activity,
    },
    {
      id: 'urgent-stat',
      label: 'Ưu tiên Cấp cứu / Khẩn',
      value: stats.urgentOrStatCount,
      hint: `${stats.mandatoryCount} ca bắt buộc có KQ để BS kết luận`,
      tone: 'border-rose-200 bg-rose-50/80 text-rose-800',
      icon: AlertTriangle,
    },
    {
      id: 'completed-skipped',
      label: 'Hoàn tất / Qua lượt / Hủy',
      value: stats.completedCount,
      hint: `${stats.completedCount} hoàn tất • ${stats.skippedCount} qua lượt • ${stats.cancelledCount} đã hủy`,
      tone: 'border-emerald-200 bg-emerald-50/80 text-emerald-800',
      icon: CheckCircle2,
    },
  ];

  return (
    <div
      className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
      data-testid="diagnostic-queue-stat-cards"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <div
            key={card.id}
            data-testid={`queue-stat-card-${card.id}`}
            className={`flex flex-col justify-between rounded-2xl border p-4 ${card.tone}`}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-semibold">{card.label}</span>
              <Icon className="h-4 w-4 shrink-0 opacity-80" aria-hidden="true" />
            </div>
            <div className="mt-2 text-3xl font-bold tabular-nums">{card.value}</div>
            <div className="mt-1 text-[11px] opacity-80">{card.hint}</div>
          </div>
        );
      })}
    </div>
  );
}
