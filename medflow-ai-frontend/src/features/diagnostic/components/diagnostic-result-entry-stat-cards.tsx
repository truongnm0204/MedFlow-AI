import { CheckCircle2, FileSignature, FlaskConical, ShieldCheck } from 'lucide-react';

import type { DiagnosticResultEntrySummaryStatsDto } from '../types/diagnostic-result-entry.types';

interface DiagnosticResultEntryStatCardsProps {
  summary: DiagnosticResultEntrySummaryStatsDto;
}

export function DiagnosticResultEntryStatCards({ summary }: DiagnosticResultEntryStatCardsProps) {
  const cards = [
    {
      id: 'ready-for-result-entry',
      label: 'Sẵn sàng nhập kết quả',
      value: summary.readyForResultEntryCount,
      hint: `trên tổng số ${summary.totalOrders} phiếu chỉ định`,
      tone: 'border-sky-200 bg-sky-50/80 text-sky-800',
      icon: FileSignature,
    },
    {
      id: 'in-progress-execution',
      label: 'Đang thực hiện kỹ thuật',
      value: summary.inProgressExecutionCount,
      hint: `${summary.urgentOrStatPendingResultCount} ca khẩn/cấp cứu chờ kết quả`,
      tone: 'border-amber-200 bg-amber-50/80 text-amber-800',
      icon: FlaskConical,
    },
    {
      id: 'pending-verification',
      label: 'Chờ duyệt / thẩm định KQ',
      value: summary.pendingVerificationCount,
      hint: `${summary.draftOrPreliminaryCount} bản nháp / sơ bộ`,
      tone: 'border-rose-200 bg-rose-50/80 text-rose-800',
      icon: ShieldCheck,
    },
    {
      id: 'finalized',
      label: 'Đã duyệt & trả kết quả',
      value: summary.finalizedCount,
      hint: 'kết quả FINALIZED / VERIFIED trong ca',
      tone: 'border-emerald-200 bg-emerald-50/80 text-emerald-800',
      icon: CheckCircle2,
    },
  ];

  return (
    <div
      className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
      data-testid="diagnostic-result-entry-stat-cards"
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
