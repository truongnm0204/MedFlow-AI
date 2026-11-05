import type { ExaminationQueueStatsDto } from '../types/examination-queue.types';

export interface ExaminationQueueStatCardsProps {
  stats: ExaminationQueueStatsDto;
}

export function ExaminationQueueStatCards({ stats }: ExaminationQueueStatCardsProps) {
  const items = [
    {
      label: 'Tổng lượt trong hàng đợi',
      value: stats.totalEntries,
      hint: `tất cả điểm dịch vụ hôm nay (${stats.activeServicePoints} phòng)`,
      tone: 'text-navy bg-white border-border',
    },
    {
      label: 'Đang chờ gọi khám',
      value: stats.waitingCount,
      hint: 'chờ gọi & sẵn sàng tại sảnh',
      tone: 'text-amber-700 bg-amber-50 border-amber-100',
    },
    {
      label: 'Đang gọi & Đang khám',
      value: stats.calledOrServingCount,
      hint: 'đang phục vụ tại các phòng',
      tone: 'text-emerald-700 bg-emerald-50 border-emerald-100',
    },
    {
      label: 'Hoàn tất / Qua lượt',
      value: stats.completedCount,
      hint: `${stats.completedCount} hoàn tất • ${stats.skippedCount} qua lượt`,
      tone: 'text-sky-700 bg-sky-50 border-sky-100',
    },
  ];

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {items.map((item) => (
        <div key={item.label} className={`rounded-2xl border p-4 shadow-sm ${item.tone}`}>
          <div className="text-[11px] font-bold uppercase tracking-wider opacity-80">
            {item.label}
          </div>
          <div className="text-3xl font-extrabold mt-1 font-mono tabular-nums">{item.value}</div>
          <div className="text-[11px] mt-0.5 opacity-75">{item.hint}</div>
        </div>
      ))}
    </div>
  );
}
