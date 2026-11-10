import { AlertTriangle } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  TrackingVisit,
  TrackingVisitStatus,
} from '../types/diagnostic-results-tracking.types';

interface DiagnosticTrackingTableProps {
  visits: TrackingVisit[];
  onViewSummary: (visit: TrackingVisit) => void;
}

const STATUS_BADGE: Record<
  TrackingVisitStatus,
  { label: string; tone: 'emerald' | 'sky' | 'amber' }
> = {
  COMPLETED: { label: 'Hoàn tất 100%', tone: 'emerald' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'sky' },
  PENDING_SIGN: { label: 'Chờ ký duyệt', tone: 'amber' },
};

export function DiagnosticTrackingTable({ visits, onViewSummary }: DiagnosticTrackingTableProps) {
  if (visits.length === 0) {
    return (
      <div className="py-10 text-center text-sm text-sub">
        Không tìm thấy lượt khám phù hợp với bộ lọc.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs">
        <thead>
          <tr className="border-b border-border text-[10px] font-bold uppercase tracking-wide text-sub">
            <th className="py-3 pr-3">MÃ LƯỢT KHÁM</th>
            <th className="py-3 pr-3">BỆNH NHÂN</th>
            <th className="py-3 pr-3">BS CHỈ ĐỊNH</th>
            <th className="py-3 pr-3">TIẾN ĐỘ HOÀN THÀNH</th>
            <th className="py-3 pr-3">TRẠNG THÁI</th>
            <th className="py-3 pr-3">CẢNH BÁO</th>
            <th className="py-3">THAO TÁC</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {visits.map((visit) => {
            const total = visit.orders.length;
            const done = visit.orders.filter((o) => o.status === 'SIGNED').length;
            const percent = total === 0 ? 0 : Math.round((done / total) * 100);
            const hasAbnormal = visit.orders.some((o) => o.isAbnormal);
            const badge = STATUS_BADGE[visit.status];
            return (
              <tr key={visit.id}>
                <td className="py-3 pr-3 font-mono font-semibold text-navy">{visit.visitCode}</td>
                <td className="py-3 pr-3">
                  <div className="font-semibold text-ink">{visit.patientName}</div>
                  <div className="font-mono text-[10px] text-sub">{visit.patientCode}</div>
                </td>
                <td className="py-3 pr-3 text-ink">{visit.orderingDoctor}</td>
                <td className="py-3 pr-3">
                  <div className="flex items-center gap-2">
                    <div
                      className="h-1.5 w-24 overflow-hidden rounded-full bg-slate-100"
                      role="progressbar"
                      aria-label={`Tiến độ ${visit.visitCode}`}
                      aria-valuemin={0}
                      aria-valuemax={100}
                      aria-valuenow={percent}
                    >
                      <div
                        className="h-full rounded-full bg-primary"
                        style={{ width: `${percent}%` }}
                      />
                    </div>
                    <span className="font-mono font-semibold tabular-nums text-ink">
                      {done}/{total}
                    </span>
                  </div>
                </td>
                <td className="py-3 pr-3">
                  <Badge tone={badge.tone}>{badge.label}</Badge>
                </td>
                <td className="py-3 pr-3">
                  {hasAbnormal ? (
                    <Badge tone="rose">
                      <AlertTriangle className="mr-1 h-3 w-3" aria-hidden="true" />
                      Bất thường
                    </Badge>
                  ) : (
                    <span className="text-sub">—</span>
                  )}
                </td>
                <td className="py-3">
                  <button
                    type="button"
                    aria-label={`Xem tổng hợp KQ ${visit.visitCode}`}
                    onClick={() => onViewSummary(visit)}
                    className="rounded-lg bg-primary px-3 py-1.5 text-[11px] font-semibold text-white transition-colors hover:bg-primary-dark"
                  >
                    Xem tổng hợp KQ
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
