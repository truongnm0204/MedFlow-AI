import { Building2 } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { DiagnosticServicePointWorklistDto } from '../types/diagnostic-worklist.types';

interface DiagnosticServicePointSummaryCardProps {
  servicePoints: DiagnosticServicePointWorklistDto[];
  selectedServicePointId: string;
  onSelectServicePoint: (servicePointId: string) => void;
}

export function DiagnosticServicePointSummaryCard({
  servicePoints,
  selectedServicePointId,
  onSelectServicePoint,
}: DiagnosticServicePointSummaryCardProps) {
  if (servicePoints.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-canvas px-4 py-6 text-center text-xs text-sub">
        Chưa có cấu hình quầy / phòng cận lâm sàng đang hoạt động.
      </div>
    );
  }

  return (
    <div className="space-y-2.5" role="list" aria-label="Điểm thực hiện cận lâm sàng">
      {servicePoints.map((sp) => {
        const isSelected = selectedServicePointId === sp.formattedServicePointId;
        return (
          <button
            key={sp.formattedServicePointId}
            type="button"
            aria-pressed={isSelected}
            onClick={() => onSelectServicePoint(isSelected ? 'ALL' : sp.formattedServicePointId)}
            className={`w-full rounded-xl border p-3 text-left transition-all ${
              isSelected
                ? 'border-primary bg-sky-50/70 ring-1 ring-primary/30'
                : 'border-border bg-white hover:border-sky-200 hover:bg-canvas'
            }`}
          >
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary border border-sky-100">
                  <Building2 className="h-4 w-4" aria-hidden="true" />
                </div>
                <div className="min-w-0">
                  <div className="flex items-center gap-1.5">
                    <span className="font-mono text-xs font-bold text-navy">{sp.roomCode}</span>
                    <Badge tone={sp.isActive ? 'emerald' : 'slate'} className="text-[10px]">
                      {sp.isActive ? 'Đang mở' : 'Tạm ngưng'}
                    </Badge>
                  </div>
                  <div className="truncate text-xs font-semibold text-ink">
                    {sp.servicePointName}
                  </div>
                </div>
              </div>

              <span className="font-mono text-xs font-bold text-primary">
                {sp.totalOrdersCount} CĐ
              </span>
            </div>

            <div className="mt-2.5 grid grid-cols-2 gap-2 rounded-lg bg-canvas px-2.5 py-1.5 text-[11px]">
              <div>
                <span className="text-sub">Đang phục vụ: </span>
                <strong className="font-mono text-navy">{sp.currentServingTicketNo ?? '—'}</strong>
              </div>
              <div>
                <span className="text-sub">Kế tiếp: </span>
                <strong className="font-mono text-primary">{sp.nextWaitingTicketNo ?? '—'}</strong>
              </div>
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-2 text-[10px] text-sub">
              <span>
                Chờ XN:{' '}
                <strong className="font-mono text-amber-700">{sp.pendingOrdersCount}</strong>
              </span>
              <span>•</span>
              <span>
                Đã XN: <strong className="font-mono text-sky-700">{sp.confirmedCount}</strong>
              </span>
              <span>•</span>
              <span>
                Đang làm:{' '}
                <strong className="font-mono text-emerald-700">{sp.inProgressCount}</strong>
              </span>
              <span>•</span>
              <span>
                Hoàn tất: <strong className="font-mono text-navy">{sp.completedCount}</strong>
              </span>
            </div>
          </button>
        );
      })}
    </div>
  );
}
