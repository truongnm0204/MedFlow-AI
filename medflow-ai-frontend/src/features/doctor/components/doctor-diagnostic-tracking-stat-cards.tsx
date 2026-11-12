import type { DiagnosticTrackingStatsDto } from '../types/doctor-diagnostic-tracking.types';

interface DoctorDiagnosticTrackingStatCardsProps {
  stats: DiagnosticTrackingStatsDto;
}

export function DoctorDiagnosticTrackingStatCards({
  stats,
}: DoctorDiagnosticTrackingStatCardsProps) {
  const cards = [
    {
      key: 'totalVisits' as const,
      label: 'LƯỢT KHÁM CÓ CHỈ ĐỊNH CLS',
      value: stats.totalVisits,
      hint: `${stats.totalOrders} phiếu chỉ định`,
      tone: 'text-navy bg-white border-border',
    },
    {
      key: 'completedVisits' as const,
      label: 'HOÀN TẤT 100%',
      value: stats.completedVisits,
      hint: `${stats.signedOrders} phiếu đã ký duyệt`,
      tone: 'text-emerald-700 bg-emerald-50 border-emerald-100',
    },
    {
      key: 'inProgressVisits' as const,
      label: 'ĐANG THỰC HIỆN',
      value: stats.inProgressVisits,
      hint: `${stats.waitingOrders} phiếu chờ kết quả`,
      tone: 'text-sky-700 bg-sky-50 border-sky-100',
    },
    {
      key: 'pendingSignVisits' as const,
      label: 'CHỜ KÝ DUYỆT',
      value: stats.pendingSignVisits,
      hint: 'Đã có kết quả, chờ ký duyệt',
      tone: 'text-amber-700 bg-amber-50 border-amber-100',
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {cards.map((card) => (
        <div
          key={card.key}
          data-testid={`tracking-stat-${card.key}`}
          className={`flex flex-col gap-1 rounded-2xl border p-4 ${card.tone}`}
        >
          <div className="text-[11px] font-bold tracking-wide">{card.label}</div>
          <div className="text-3xl font-bold tabular-nums">
            {String(card.value).padStart(2, '0')}
          </div>
          <div className="text-[11px] opacity-70">{card.hint}</div>
        </div>
      ))}
    </div>
  );
}
