import type { DiagnosticStats } from '../types/diagnostic.types';

interface DiagnosticStatCardsProps {
  stats: DiagnosticStats;
}

const CARDS = [
  {
    key: 'pendingOrders' as const,
    label: 'Chờ xác nhận chỉ định',
    hint: 'phiếu chỉ định mới từ phòng khám',
    tone: 'text-amber-600 bg-amber-50 border-amber-100',
  },
  {
    key: 'inQueue' as const,
    label: 'Hàng đợi thực hiện',
    hint: 'bệnh nhân đã tiếp nhận chờ gọi',
    tone: 'text-sky-600 bg-sky-50 border-sky-100',
  },
  {
    key: 'inExecution' as const,
    label: 'Đang thực hiện CLS',
    hint: 'đang lấy mẫu / chụp chiếu',
    tone: 'text-emerald-600 bg-emerald-50 border-emerald-100',
  },
  {
    key: 'completedToday' as const,
    label: 'Đã trả kết quả hôm nay',
    hint: 'ca cận lâm sàng đã hoàn tất',
    tone: 'text-slate-600 bg-slate-50 border-slate-100',
  },
];

export function DiagnosticStatCards({ stats }: DiagnosticStatCardsProps) {
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
