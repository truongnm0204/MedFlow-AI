import type { PharmacyStats } from '../types/pharmacy.types';

interface PharmacyStatCardsProps {
  stats: PharmacyStats;
}

const STAT_CARDS = [
  {
    key: 'pendingPrescriptions' as const,
    label: 'Đơn chờ tiếp nhận',
    hint: 'bác sĩ vừa kê chuyển xuống',
    tone: 'border-amber-200 bg-amber-50 text-amber-700',
  },
  {
    key: 'preparingCount' as const,
    label: 'Đang soạn thuốc (FEFO)',
    hint: 'đang kiểm lô & đóng gói tại quầy',
    tone: 'border-sky-200 bg-sky-50 text-sky-700',
  },
  {
    key: 'readyToDispense' as const,
    label: 'Chờ gọi BN nhận thuốc',
    hint: 'đã soạn xong chờ bàn giao',
    tone: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  },
  {
    key: 'dispensedToday' as const,
    label: 'Đã cấp phát hôm nay',
    hint: 'đơn hoàn tất xuất kho trong ca',
    tone: 'border-border bg-white text-ink',
  },
];

export function PharmacyStatCards({ stats }: PharmacyStatCardsProps) {
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {STAT_CARDS.map((card) => (
        <div
          key={card.key}
          className={`flex flex-col gap-1 rounded-2xl border p-4 shadow-2xs ${card.tone}`}
        >
          <div className="font-mono text-3xl font-bold tabular-nums">
            {String(stats[card.key]).padStart(2, '0')}
          </div>
          <div className="text-xs font-bold">{card.label}</div>
          <div className="text-[11px] opacity-80">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
