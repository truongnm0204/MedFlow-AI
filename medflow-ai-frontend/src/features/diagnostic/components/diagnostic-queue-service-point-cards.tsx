import { Building2, Megaphone, PlayCircle, UserCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type {
  DiagnosticCategory,
  DiagnosticServicePointQueueBucketDto,
} from '../types/diagnostic.types';

interface DiagnosticQueueServicePointCardsProps {
  buckets: DiagnosticServicePointQueueBucketDto[];
  selectedServicePointId: string;
  hasManagePermission: boolean;
  callingServicePointId?: string | null;
  onSelectServicePoint: (servicePointId: string) => void;
  onCallNext: (bucket: DiagnosticServicePointQueueBucketDto) => void;
}

const CATEGORY_BADGE_TONE: Record<DiagnosticCategory, 'sky' | 'emerald' | 'amber'> = {
  LABORATORY: 'sky',
  IMAGING: 'emerald',
  FUNCTIONAL: 'amber',
};

export function DiagnosticQueueServicePointCards({
  buckets,
  selectedServicePointId,
  hasManagePermission,
  callingServicePointId,
  onSelectServicePoint,
  onCallNext,
}: DiagnosticQueueServicePointCardsProps) {
  if (buckets.length === 0) {
    return (
      <div
        className="rounded-2xl border border-dashed border-border bg-canvas px-4 py-6 text-center text-xs text-sub"
        data-testid="diagnostic-service-points-empty"
      >
        Chưa có cấu hình điểm phục vụ cận lâm sàng đang hoạt động.
      </div>
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-3 md:grid-cols-3"
      role="list"
      aria-label="Bảng điểm phục vụ hàng đợi cận lâm sàng"
      data-testid="diagnostic-queue-service-point-cards"
    >
      {buckets.map((bucket) => {
        const isSelected =
          selectedServicePointId.toUpperCase() === bucket.formattedServicePointId.toUpperCase() ||
          selectedServicePointId === String(bucket.servicePointId);
        const isBusy = bucket.calledCount > 0 || bucket.inProgressCount > 0;
        const isCallingThis = callingServicePointId === bucket.formattedServicePointId;

        return (
          <div
            key={bucket.formattedServicePointId}
            role="listitem"
            data-testid={`service-point-bucket-${bucket.formattedServicePointId}`}
            className={`flex flex-col justify-between rounded-2xl border p-4 transition-all ${
              isSelected
                ? 'border-primary bg-sky-50/70 ring-1 ring-primary/30'
                : 'border-border bg-white hover:border-sky-300'
            }`}
          >
            <div>
              <div className="flex items-start justify-between gap-2">
                <button
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() =>
                    onSelectServicePoint(isSelected ? 'ALL' : bucket.formattedServicePointId)
                  }
                  className="text-left focus:outline-none"
                >
                  <div className="flex items-center gap-1.5">
                    <Building2 className="h-4 w-4 text-primary" aria-hidden="true" />
                    <span className="font-mono text-xs font-bold text-navy">{bucket.roomCode}</span>
                    <span className="text-[11px] font-medium text-sub">
                      ({bucket.formattedServicePointId})
                    </span>
                  </div>
                  <div className="mt-0.5 text-xs font-bold text-ink">{bucket.servicePointName}</div>
                  {bucket.roomLocation && (
                    <div className="text-[10px] text-sub">{bucket.roomLocation}</div>
                  )}
                </button>

                <Badge tone={CATEGORY_BADGE_TONE[bucket.category] ?? 'sky'}>
                  {bucket.categoryLabel}
                </Badge>
              </div>

              {/* Current Serving vs Next Waiting */}
              <div className="mt-3 grid grid-cols-2 gap-2 rounded-xl border border-border bg-canvas p-2.5 text-xs">
                <div>
                  <div className="flex items-center gap-1 text-[10px] font-semibold uppercase text-sub">
                    <PlayCircle className="h-3 w-3 text-emerald-600" aria-hidden="true" />
                    <span>Đang phục vụ</span>
                  </div>
                  {bucket.currentServingEntry ? (
                    <div className="mt-1">
                      <span className="font-mono text-sm font-bold text-emerald-700">
                        {bucket.currentServingEntry.ticketNo}
                      </span>
                      <div className="truncate text-[11px] font-medium text-navy">
                        {bucket.currentServingEntry.patientName}
                      </div>
                    </div>
                  ) : (
                    <div className="mt-1 text-[11px] italic text-sub">Phòng đang trống</div>
                  )}
                </div>

                <div>
                  <div className="flex items-center gap-1 text-[10px] font-semibold uppercase text-sub">
                    <UserCheck className="h-3 w-3 text-sky-600" aria-hidden="true" />
                    <span>Kế tiếp đủ ĐK</span>
                  </div>
                  {bucket.nextWaitingEntry ? (
                    <div className="mt-1">
                      <span className="font-mono text-sm font-bold text-sky-700">
                        {bucket.nextWaitingEntry.ticketNo}
                      </span>
                      <div className="truncate text-[11px] font-medium text-navy">
                        {bucket.nextWaitingEntry.patientName}
                      </div>
                    </div>
                  ) : (
                    <div className="mt-1 text-[11px] italic text-sub">Không có BN chờ</div>
                  )}
                </div>
              </div>

              {/* Counts breakdown */}
              <div className="mt-2.5 flex flex-wrap items-center justify-between gap-1 text-[11px] text-sub">
                <span>
                  Sẵn sàng: <strong className="font-mono text-navy">{bucket.readyCount}</strong>
                </span>
                <span>
                  Đang chờ: <strong className="font-mono text-navy">{bucket.waitingCount}</strong>
                </span>
                <span>
                  Đang TH:{' '}
                  <strong className="font-mono text-navy">
                    {bucket.calledCount + bucket.inProgressCount}
                  </strong>
                </span>
                <span>
                  Qua lượt: <strong className="font-mono text-navy">{bucket.skippedCount}</strong>
                </span>
              </div>
            </div>

            {/* Footer actions */}
            <div className="mt-3 flex items-center justify-between gap-2 border-t border-border pt-2.5">
              <button
                type="button"
                onClick={() =>
                  onSelectServicePoint(isSelected ? 'ALL' : bucket.formattedServicePointId)
                }
                className="text-[11px] font-semibold text-primary hover:underline"
              >
                {isSelected ? 'Bỏ lọc phòng này' : `Lọc phòng (${bucket.totalEntriesCount})`}
              </button>

              {hasManagePermission && (
                <Button
                  type="button"
                  size="sm"
                  variant={isBusy ? 'secondary' : 'primary'}
                  loading={isCallingThis}
                  disabled={isCallingThis || !bucket.isActive || !bucket.isRoomActive}
                  onClick={() => onCallNext(bucket)}
                  data-testid={`call-next-btn-${bucket.formattedServicePointId}`}
                >
                  <Megaphone className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Gọi BN tiếp theo</span>
                </Button>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
