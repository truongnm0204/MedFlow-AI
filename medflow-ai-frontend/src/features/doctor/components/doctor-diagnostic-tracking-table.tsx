import { ChevronLeft, ChevronRight } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  DiagnosticTrackingVisitDto,
  DiagnosticTrackingVisitStatus,
} from '../types/doctor-diagnostic-tracking.types';

interface DoctorDiagnosticTrackingTableProps {
  visits: DiagnosticTrackingVisitDto[];
  page: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onViewSummary: (visit: DiagnosticTrackingVisitDto) => void;
}

const STATUS_BADGE: Record<
  DiagnosticTrackingVisitStatus,
  { label: string; tone: 'emerald' | 'sky' | 'amber' }
> = {
  COMPLETED: { label: 'Hoàn tất 100%', tone: 'emerald' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'sky' },
  PENDING_SIGN: { label: 'Chờ ký duyệt', tone: 'amber' },
};

function formatTime(isoOrText: string | null): string {
  if (!isoOrText) return '—';
  if (/^\d{2}:\d{2}$/.test(isoOrText.trim())) return isoOrText.trim();
  const parsed = new Date(isoOrText);
  if (Number.isNaN(parsed.getTime())) return isoOrText;
  return parsed.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

export function DoctorDiagnosticTrackingTable({
  visits,
  page,
  totalPages,
  totalCount,
  onPageChange,
  onViewSummary,
}: DoctorDiagnosticTrackingTableProps) {
  if (visits.length === 0) {
    return (
      <div className="py-10 text-center text-sm text-sub">
        Không tìm thấy lượt khám phù hợp với bộ lọc.
      </div>
    );
  }

  return (
    <div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="border-b border-border text-[10px] font-bold uppercase tracking-wide text-sub">
              <th className="py-3 pr-3">MÃ LƯỢT KHÁM</th>
              <th className="py-3 pr-3">BỆNH NHÂN</th>
              <th className="py-3 pr-3">CHỈ ĐỊNH GẦN NHẤT</th>
              <th className="py-3 pr-3">TIẾN ĐỘ HOÀN THÀNH</th>
              <th className="py-3 pr-3">TRẠNG THÁI</th>
              <th className="py-3">THAO TÁC</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {visits.map((visit) => {
              const badge = STATUS_BADGE[visit.trackingStatus];
              return (
                <tr key={visit.visitId}>
                  <td className="py-3 pr-3 font-mono font-semibold text-navy">{visit.visitCode}</td>
                  <td className="py-3 pr-3">
                    <div className="font-semibold text-ink">{visit.patientName ?? '—'}</div>
                    <div className="font-mono text-[10px] text-sub">{visit.patientCode ?? '—'}</div>
                  </td>
                  <td className="py-3 pr-3 font-mono text-ink">
                    {formatTime(visit.latestOrderedAt)}
                  </td>
                  <td className="py-3 pr-3">
                    <div className="flex items-center gap-2">
                      <div
                        className="h-1.5 w-24 overflow-hidden rounded-full bg-slate-100"
                        role="progressbar"
                        aria-label={`Tiến độ ${visit.visitCode}`}
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={visit.completionPercent}
                      >
                        <div
                          className="h-full rounded-full bg-primary"
                          style={{ width: `${visit.completionPercent}%` }}
                        />
                      </div>
                      <span className="font-mono font-semibold tabular-nums text-ink">
                        {visit.signedOrders}/{visit.totalOrders}
                      </span>
                    </div>
                    {visit.mandatoryOrders > 0 && (
                      <div className="mt-0.5 text-[10px] text-sub">
                        Bắt buộc: {visit.mandatorySignedOrders}/{visit.mandatoryOrders}
                      </div>
                    )}
                  </td>
                  <td className="py-3 pr-3">
                    <Badge tone={badge.tone}>{badge.label}</Badge>
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

      {totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-border pt-3 text-xs text-sub">
          <span>{totalCount} lượt khám</span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              aria-label="Trang trước"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
              className="rounded-md border border-border p-1.5 text-navy hover:bg-canvas disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <span>
              Trang {page} / {totalPages}
            </span>
            <button
              type="button"
              aria-label="Trang sau"
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
              className="rounded-md border border-border p-1.5 text-navy hover:bg-canvas disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
