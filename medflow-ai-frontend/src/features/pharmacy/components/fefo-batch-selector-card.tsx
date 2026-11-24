import { AlertTriangle, CheckCircle2, Lock, MapPin, ShieldAlert } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import {
  isRequirementFefoOverride,
  type BatchStatus,
  type FefoBatchRecord,
  type MedicineBatchRequirement,
} from '../types/fefo-batch.types';

interface FefoBatchSelectorCardProps {
  requirement: MedicineBatchRequirement;
  onSelectBatch: (requirementId: string, batch: FefoBatchRecord) => void;
  onOverrideReasonChange?: (requirementId: string, reason: string) => void;
}

const STATUS_META: Record<
  BatchStatus,
  { label: string; tone: 'emerald' | 'sky' | 'rose' | 'amber' }
> = {
  FEFO_RECOMMENDED: { label: 'Ưu tiên FEFO', tone: 'emerald' },
  AVAILABLE: { label: 'Khả dụng', tone: 'sky' },
  EXPIRED: { label: 'Hết hạn (Khóa)', tone: 'rose' },
  BLOCKED: { label: 'Bị chặn xuất', tone: 'amber' },
};

export function FefoBatchSelectorCard({
  requirement,
  onSelectBatch,
  onOverrideReasonChange,
}: FefoBatchSelectorCardProps) {
  const requiresOverrideReason = isRequirementFefoOverride(requirement);
  const reasonInputId = `fefo-override-reason-${requirement.id}`;

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
        <div>
          <h2 className="text-sm font-bold text-navy">{requirement.drugName}</h2>
          <p className="text-[11px] text-sub">{requirement.dosageForm}</p>
        </div>
        <Badge tone="sky">
          Cần lấy:{' '}
          <strong className="ml-1 font-mono">
            {requirement.requiredQty} {requirement.unit}
          </strong>
        </Badge>
      </CardHeader>

      <CardContent className="space-y-2.5">
        {requirement.batches.map((batch) => {
          const isBlocked =
            batch.status === 'EXPIRED' ||
            batch.status === 'BLOCKED' ||
            batch.isSelectable === false;
          const isSelected = requirement.selectedBatchId === batch.id;
          const meta = STATUS_META[batch.status];

          return (
            <button
              key={batch.id}
              type="button"
              disabled={isBlocked}
              onClick={() => onSelectBatch(requirement.id, batch)}
              aria-pressed={isSelected}
              className={`w-full rounded-xl border p-3.5 text-left transition-all flex flex-col sm:flex-row sm:items-center justify-between gap-3 ${
                isBlocked
                  ? 'cursor-not-allowed border-rose-200 bg-rose-50/50 opacity-75'
                  : isSelected
                    ? 'border-primary bg-sky-50/50 ring-2 ring-primary shadow-2xs'
                    : 'border-border bg-white hover:border-slate-300'
              }`}
            >
              <div className="space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-mono text-xs font-bold text-navy">{batch.batchCode}</span>
                  <Badge tone={meta.tone}>{meta.label}</Badge>
                  <span className="text-[11px] text-sub">• NSX: {batch.manufacturer}</span>
                </div>

                <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-ink">
                  <span>
                    HSD: <strong className="font-mono">{batch.expiryDate}</strong>
                  </span>
                  <span>
                    Tồn kho:{' '}
                    <strong className="font-mono">
                      {batch.availableQty} {batch.unit}
                    </strong>
                  </span>
                  <span className="inline-flex items-center gap-1 text-sub">
                    <MapPin className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                    {batch.shelfLocation}
                  </span>
                </div>

                {batch.blockReason && (
                  <p className="flex items-center gap-1 text-[11px] font-semibold text-rose-700">
                    <ShieldAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>{batch.blockReason}</span>
                  </p>
                )}
              </div>

              <div className="shrink-0 self-end sm:self-center">
                {isBlocked ? (
                  <span className="inline-flex items-center gap-1 rounded-lg bg-rose-100 px-2.5 py-1 text-[11px] font-bold text-rose-700">
                    <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                    Không thể chọn
                  </span>
                ) : isSelected ? (
                  <span className="inline-flex items-center gap-1 rounded-lg bg-primary px-3 py-1 text-[11px] font-bold text-white">
                    <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                    Đã chọn
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-3 py-1 text-[11px] font-semibold text-navy">
                    <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-hidden="true" />
                    Chọn lô này
                  </span>
                )}
              </div>
            </button>
          );
        })}

        {requiresOverrideReason && (
          <div className="mt-2 rounded-xl border border-amber-200 bg-amber-50/70 p-3.5 space-y-2">
            <label
              htmlFor={reasonInputId}
              className="flex items-center gap-1.5 text-xs font-bold text-amber-900"
            >
              <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-amber-600" aria-hidden="true" />
              <span>Lý do chọn lô hạn dùng xa hơn lô ưu tiên FEFO (Bắt buộc)</span>
            </label>
            <input
              id={reasonInputId}
              type="text"
              value={requirement.overrideReason ?? ''}
              onChange={(e) => onOverrideReasonChange?.(requirement.id, e.target.value)}
              maxLength={500}
              placeholder="Nhập lý do chuyên môn (tối thiểu 5 ký tự, VD: lô hạn gần đang kiểm kê)..."
              className="w-full rounded-lg border border-amber-300 bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
}
