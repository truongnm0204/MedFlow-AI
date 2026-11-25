import type { ReceptionStats } from '../types/reception.types';

interface ReceptionStatCardsProps {
  stats: ReceptionStats | null;
  isLoading?: boolean;
  isError?: boolean;
}

const CARDS = [
  {
    key: 'waitingCheckIn' as const,
    label: 'Chờ check-in',
    hint: 'lịch hẹn hôm nay chưa tiếp nhận',
    tone: 'text-amber-700 bg-amber-50 border-amber-200/80',
  },
  {
    key: 'inQueue' as const,
    label: 'Đang trong hàng đợi',
    hint: 'đã check-in hoặc chờ khám lại sau CLS',
    tone: 'text-sky-700 bg-sky-50 border-sky-200/80',
  },
  {
    key: 'inConsultation' as const,
    label: 'Đang khám',
    hint: 'đang được bác sĩ trực tiếp khám',
    tone: 'text-emerald-700 bg-emerald-50 border-emerald-200/80',
  },
  {
    key: 'completedToday' as const,
    label: 'Hoàn tất hôm nay',
    hint: 'lượt khám hoàn thành trong ngày',
    tone: 'text-ink bg-white border-border',
  },
];

export function ReceptionStatCards({
  stats,
  isLoading = false,
  isError = false,
}: ReceptionStatCardsProps) {
  const showSkeleton = isLoading && stats === null;
  const hasRealStats = stats !== null && !isError;

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {CARDS.map((card) => (
        <div
          key={card.key}
          data-testid={`reception-stat-${card.key}`}
          className={`flex flex-col gap-1 rounded-2xl border p-4 shadow-2xs ${card.tone}`}
        >
          {showSkeleton ? (
            <div
              role="status"
              aria-busy="true"
              aria-label={`Đang tải ${card.label}`}
              data-testid={`stat-skeleton-${card.key}`}
              className="h-9 w-16 animate-pulse rounded-lg bg-slate-200/80 motion-reduce:animate-none"
            />
          ) : hasRealStats ? (
            <div className="text-3xl font-bold tabular-nums">{stats[card.key]}</div>
          ) : (
            <div
              className="text-3xl font-bold tabular-nums opacity-70"
              aria-label={`${card.label}: chưa có dữ liệu`}
            >
              —
            </div>
          )}
          <div className="text-xs font-semibold">{card.label}</div>
          <div className="text-[10px] opacity-75">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
