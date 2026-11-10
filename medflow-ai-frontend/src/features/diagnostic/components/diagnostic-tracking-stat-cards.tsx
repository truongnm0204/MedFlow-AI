import type { TrackingStats } from '../types/diagnostic-results-tracking.types';

interface DiagnosticTrackingStatCardsProps {
  stats: TrackingStats;
}

const CARDS = [
  {
    key: 'totalOrders' as const,
    label: 'TỔNG LƯỢT CHỈ ĐỊNH CLS',
    hint: 'Lượt khám có chỉ định',
    tone: 'text-navy bg-white border-border',
  },
  {
    key: 'completed' as const,
    label: 'HOÀN TẤT 100%',
    hint: 'Lượt đã trả đủ KQ',
    tone: 'text-emerald-700 bg-emerald-50 border-emerald-100',
  },
  {
    key: 'waiting' as const,
    label: 'ĐANG CHỜ KẾT QUẢ',
    hint: 'Lượt đang thực hiện / chờ duyệt',
    tone: 'text-amber-700 bg-amber-50 border-amber-100',
  },
  {
    key: 'abnormal' as const,
    label: 'CẢNH BÁO BẤT THƯỜNG',
    hint: 'Ca có chỉ số nguy cấp/dương tính',
    tone: 'text-rose-700 bg-rose-50 border-rose-100',
  },
];

export function DiagnosticTrackingStatCards({ stats }: DiagnosticTrackingStatCardsProps) {
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {CARDS.map((card) => (
        <div
          key={card.key}
          data-testid={`tracking-stat-${card.key}`}
          className={`flex flex-col gap-1 rounded-2xl border p-4 ${card.tone}`}
        >
          <div className="text-[11px] font-bold tracking-wide">{card.label}</div>
          <div className="text-3xl font-bold tabular-nums">
            {String(stats[card.key]).padStart(2, '0')}
          </div>
          <div className="text-[11px] opacity-70">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
