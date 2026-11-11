import { AlertTriangle, CheckCircle2, Clock, UserCheck } from 'lucide-react';

import type { DiagnosticPatientReceiveSummaryStatsDto } from '../types/diagnostic-patient-receive.types';

interface DiagnosticPatientReceiveStatCardsProps {
  summary: DiagnosticPatientReceiveSummaryStatsDto;
}

export function DiagnosticPatientReceiveStatCards({
  summary,
}: DiagnosticPatientReceiveStatCardsProps) {
  const inProgressOrCompleted = summary.inProgressCount + summary.completedCount;

  const cards = [
    {
      id: 'waiting-reception',
      label: 'Chờ tiếp nhận tại điểm DV',
      value: summary.waitingReceptionCount,
      hint: `trên tổng số ${summary.totalOrders} phiếu chỉ định`,
      tone: 'border-amber-200 bg-amber-50/80 text-amber-800',
      icon: Clock,
    },
    {
      id: 'urgent-stat-waiting',
      label: 'Khẩn / Cấp cứu STAT',
      value: summary.urgentOrStatWaitingCount,
      hint: 'cần tiếp nhận & xếp hàng đợi ưu tiên',
      tone: 'border-rose-200 bg-rose-50/80 text-rose-800',
      icon: AlertTriangle,
    },
    {
      id: 'ready-in-queue',
      label: 'Đã tiếp nhận / Sẵn sàng (READY)',
      value: summary.readyInQueueCount,
      hint: 'đang chờ gọi vào phòng thực hiện',
      tone: 'border-sky-200 bg-sky-50/80 text-sky-800',
      icon: UserCheck,
    },
    {
      id: 'in-progress-completed',
      label: 'Đang thực hiện / Đã có KQ',
      value: inProgressOrCompleted,
      hint: `${summary.inProgressCount} đang thực hiện • ${summary.completedCount} hoàn tất`,
      tone: 'border-emerald-200 bg-emerald-50/80 text-emerald-800',
      icon: CheckCircle2,
    },
  ];

  return (
    <div
      className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
      data-testid="diagnostic-receive-stat-cards"
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
