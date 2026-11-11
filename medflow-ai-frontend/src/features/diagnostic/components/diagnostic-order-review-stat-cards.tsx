import { AlertTriangle, CheckCircle2, ClipboardCheck, Clock } from 'lucide-react';

import type { DiagnosticOrderReviewSummaryStatsDto } from '../types/diagnostic-order-review.types';

interface DiagnosticOrderReviewStatCardsProps {
  summary: DiagnosticOrderReviewSummaryStatsDto;
}

export function DiagnosticOrderReviewStatCards({ summary }: DiagnosticOrderReviewStatCardsProps) {
  const inProgressOrCompleted = summary.inProgressCount + summary.completedCount;

  const cards = [
    {
      id: 'pending-confirmation',
      label: 'Chờ xác nhận',
      value: summary.pendingConfirmationCount,
      hint: `trên tổng số ${summary.totalOrders} phiếu chỉ định`,
      tone: 'border-amber-200 bg-amber-50/80 text-amber-800',
      icon: Clock,
    },
    {
      id: 'urgent-stat-pending',
      label: 'Khẩn / Cấp cứu STAT',
      value: summary.urgentOrStatPendingCount,
      hint: 'cần kiểm tra & phân luồng ưu tiên',
      tone: 'border-rose-200 bg-rose-50/80 text-rose-800',
      icon: AlertTriangle,
    },
    {
      id: 'confirmed-orders',
      label: 'Đã xác nhận (CONFIRMED)',
      value: summary.confirmedCount,
      hint: 'đã xếp hàng đợi phòng thực hiện',
      tone: 'border-sky-200 bg-sky-50/80 text-sky-800',
      icon: ClipboardCheck,
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
      data-testid="diagnostic-review-stat-cards"
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
