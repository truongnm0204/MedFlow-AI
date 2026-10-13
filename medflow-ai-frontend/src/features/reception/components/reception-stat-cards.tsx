import type { ReceptionStats } from '../types/reception.types';

interface ReceptionStatCardsProps {
  stats: ReceptionStats;
}

const CARDS = [
  {
    key: 'waitingCheckIn' as const,
    label: 'Chờ check-in',
    hint: 'lịch hẹn hôm nay chưa tiếp nhận',
    tone: 'text-amber-600 bg-amber-50 border-amber-100',
  },
  {
    key: 'inQueue' as const,
    label: 'Đang trong hàng đợi',
    hint: 'đã check-in, chờ gọi số',
    tone: 'text-sky-600 bg-sky-50 border-sky-100',
  },
  {
    key: 'inConsultation' as const,
    label: 'Đang khám',
    hint: 'đang trong phòng khám',
    tone: 'text-emerald-600 bg-emerald-50 border-emerald-100',
  },
  {
    key: 'completedToday' as const,
    label: 'Hoàn tất hôm nay',
    hint: 'lượt khám đã kết thúc',
    tone: 'text-slate-600 bg-slate-50 border-slate-100',
  },
];

export function ReceptionStatCards({ stats }: ReceptionStatCardsProps) {
  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {CARDS.map((card) => (
        <div key={card.key} className={`rounded-2xl border p-4 ${card.tone} flex flex-col gap-1`}>
          <div className="text-3xl font-bold tabular-nums">{stats[card.key]}</div>
          <div className="text-xs font-semibold">{card.label}</div>
          <div className="text-[10px] opacity-70">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
