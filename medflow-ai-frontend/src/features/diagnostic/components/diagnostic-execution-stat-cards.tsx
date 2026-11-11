import { AlertTriangle, CheckCircle2, PlayCircle, Timer } from 'lucide-react';

import type { DiagnosticServiceExecutionSummaryStatsDto } from '../types/diagnostic-execution.types';

interface DiagnosticExecutionStatCardsProps {
  summary: DiagnosticServiceExecutionSummaryStatsDto;
}

export function DiagnosticExecutionStatCards({ summary }: DiagnosticExecutionStatCardsProps) {
  const cards = [
    {
      id: 'waiting-execution',
      label: 'Chờ thực hiện (CONFIRMED)',
      value: summary.waitingExecutionCount,
      hint: `trên tổng số ${summary.totalOrders} phiếu chỉ định`,
      tone: 'border-amber-200 bg-amber-50/80 text-amber-800',
      icon: Timer,
    },
    {
      id: 'ready-to-start',
      label: 'Sẵn sàng bắt đầu',
      value: summary.readyToStartCount,
      hint: 'hàng đợi hợp lệ, có thể bắt đầu kỹ thuật',
      tone: 'border-sky-200 bg-sky-50/80 text-sky-800',
      icon: PlayCircle,
    },
    {
      id: 'in-progress',
      label: 'Đang thực hiện',
      value: summary.inProgressCount,
      hint: 'đang lấy mẫu / chụp chiếu / thăm dò',
      tone: 'border-emerald-200 bg-emerald-50/80 text-emerald-800',
      icon: CheckCircle2,
    },
    {
      id: 'completed',
      label: 'Đã hoàn tất',
      value: summary.completedCount,
      hint: 'kỹ thuật xong, chờ nhập kết quả (UC43)',
      tone: 'border-slate-200 bg-slate-50/80 text-slate-800',
      icon: CheckCircle2,
    },
    {
      id: 'urgent-stat',
      label: 'Khẩn / Cấp tốc STAT',
      value: summary.urgentOrStatCount,
      hint: 'phiếu ưu tiên cần theo dõi thực hiện',
      tone: 'border-rose-200 bg-rose-50/80 text-rose-800',
      icon: AlertTriangle,
    },
  ];

  return (
    <div
      className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5"
      data-testid="diagnostic-execution-stat-cards"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <div
            key={card.id}
            data-testid={`stat-card-${card.id}`}
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
