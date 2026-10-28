import type { DoctorStats } from '../types/doctor.types';

interface DoctorStatCardsProps {
  stats: DoctorStats;
}

const STAT_CARDS = [
  {
    key: 'assignedWaiting' as const,
    label: 'Chờ khám tại phòng',
    hint: 'bệnh nhân đã phân công vào P.102',
    tone: 'text-sky-600 bg-sky-50 border-sky-100',
  },
  {
    key: 'inConsultation' as const,
    label: 'Đang khám lâm sàng',
    hint: 'ca đang mở hồ sơ tại bàn khám',
    tone: 'text-emerald-600 bg-emerald-50 border-emerald-100',
  },
  {
    key: 'pendingLabResults' as const,
    label: 'Chờ duyệt kết quả CLS',
    hint: 'ca cận lâm sàng cần đọc kết quả',
    tone: 'text-amber-600 bg-amber-50 border-amber-100',
  },
  {
    key: 'completedToday' as const,
    label: 'Hoàn tất trong ca',
    hint: 'lượt khám đã kết luận & kê đơn',
    tone: 'text-slate-600 bg-slate-50 border-slate-100',
  },
];

export function DoctorStatCards({ stats }: DoctorStatCardsProps) {
  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {STAT_CARDS.map((card) => (
        <div key={card.key} className={`rounded-2xl border p-4 ${card.tone} flex flex-col gap-1`}>
          <div className="text-3xl font-bold tabular-nums">{stats[card.key]}</div>
          <div className="text-xs font-semibold">{card.label}</div>
          <div className="text-[10px] opacity-70">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
