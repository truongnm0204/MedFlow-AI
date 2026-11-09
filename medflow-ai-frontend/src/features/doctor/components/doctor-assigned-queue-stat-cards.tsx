import type { DoctorAssignedQueueStatsDto } from '../types/doctor-assigned-queue.types';

interface DoctorAssignedQueueStatCardsProps {
  stats: DoctorAssignedQueueStatsDto;
}

export function DoctorAssignedQueueStatCards({ stats }: DoctorAssignedQueueStatCardsProps) {
  const cards = [
    {
      key: 'serving',
      label: 'Đang khám / đã gọi',
      value: stats.inProgressCount + stats.calledCount,
      hint: 'ca đang ở trong phòng khám',
      tone: 'text-emerald-600 bg-emerald-50 border-emerald-100',
    },
    {
      key: 'eligible',
      label: 'Đủ điều kiện gọi',
      value: stats.eligibleCount,
      hint: 'sẵn sàng gọi theo thứ tự Backend',
      tone: 'text-sky-600 bg-sky-50 border-sky-100',
    },
    {
      key: 'notEligible',
      label: 'Chưa đủ điều kiện',
      value: stats.notEligibleCount,
      hint: 'chưa đến / giữ chỗ / chờ CLS',
      tone: 'text-amber-600 bg-amber-50 border-amber-100',
    },
    {
      key: 'completed',
      label: 'Hoàn tất',
      value: stats.completedCount,
      hint: 'lượt khám đã kết thúc trong ngày',
      tone: 'text-slate-600 bg-slate-50 border-slate-100',
    },
  ];

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {cards.map((card) => (
        <div key={card.key} className={`rounded-2xl border p-4 ${card.tone} flex flex-col gap-1`}>
          <div className="text-3xl font-bold tabular-nums">{card.value}</div>
          <div className="text-xs font-semibold">{card.label}</div>
          <div className="text-[10px] opacity-70">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
