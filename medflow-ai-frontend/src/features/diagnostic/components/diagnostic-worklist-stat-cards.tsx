import { Badge } from '@/shared/ui/badge';

import type { DiagnosticWorklistStatsDto } from '../types/diagnostic-worklist.types';

interface DiagnosticWorklistStatCardsProps {
  stats: DiagnosticWorklistStatsDto;
}

const PRIMARY_CARDS = [
  {
    key: 'pendingOrders' as const,
    label: 'Chờ xác nhận chỉ định',
    hint: 'phiếu chỉ định mới cần tiếp nhận (UC39)',
    tone: 'text-amber-700 bg-amber-50 border-amber-200',
  },
  {
    key: 'inQueue' as const,
    label: 'Hàng đợi thực hiện',
    hint: 'đã xác nhận & đang xếp hàng phòng kỹ thuật',
    tone: 'text-sky-700 bg-sky-50 border-sky-200',
  },
  {
    key: 'inExecution' as const,
    label: 'Đang thực hiện CLS',
    hint: 'đang lấy mẫu / chụp chiếu / thăm dò (UC42)',
    tone: 'text-emerald-700 bg-emerald-50 border-emerald-200',
  },
  {
    key: 'completedToday' as const,
    label: 'Đã trả kết quả hôm nay',
    hint: 'chỉ định hoàn tất & đồng bộ về bác sĩ (UC43)',
    tone: 'text-navy bg-white border-border',
  },
];

export function DiagnosticWorklistStatCards({ stats }: DiagnosticWorklistStatCardsProps) {
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {PRIMARY_CARDS.map((card) => (
          <div
            key={card.key}
            data-testid={`worklist-stat-${card.key}`}
            className={`flex flex-col gap-1 rounded-2xl border p-4 shadow-2xs ${card.tone}`}
          >
            <div className="text-3xl font-bold tabular-nums">{stats[card.key]}</div>
            <div className="text-xs font-semibold">{card.label}</div>
            <div className="text-[10px] opacity-80">{card.hint}</div>
          </div>
        ))}
      </div>

      <div
        aria-label="Chỉ số phân bổ danh sách công việc CLS"
        className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-white px-4 py-2.5 text-xs"
      >
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold text-navy">
            Tổng số phiếu: <span className="font-mono font-bold">{stats.totalOrders}</span>
          </span>
          <Badge tone="rose">Cấp cứu STAT: {stats.statOrders}</Badge>
          <Badge tone="amber">Khẩn: {stats.urgentOrders}</Badge>
          <Badge tone="sky">Bắt buộc kết luận: {stats.mandatoryOrders}</Badge>
          {stats.pendingResultVerificationCount > 0 && (
            <Badge tone="emerald">Chờ duyệt KQ: {stats.pendingResultVerificationCount}</Badge>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2 text-[11px] text-sub">
          <span>
            Xét nghiệm: <strong className="font-mono text-navy">{stats.laboratoryCount}</strong>
          </span>
          <span>•</span>
          <span>
            Chẩn đoán hình ảnh:{' '}
            <strong className="font-mono text-navy">{stats.imagingCount}</strong>
          </span>
          <span>•</span>
          <span>
            Thăm dò chức năng:{' '}
            <strong className="font-mono text-navy">{stats.functionalCount}</strong>
          </span>
        </div>
      </div>
    </div>
  );
}
