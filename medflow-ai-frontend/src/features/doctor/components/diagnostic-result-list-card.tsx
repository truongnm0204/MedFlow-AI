import { CheckCircle2, Eye } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { ConsultationDiagnosticResultItemDto } from '../types/review-diagnostic.types';
import { getResultStatusBadge } from '../utils/review-diagnostic-meta';

export interface DiagnosticResultListCardProps {
  result: ConsultationDiagnosticResultItemDto;
  index: number;
  isSelected: boolean;
  onSelect: (orderId: number) => void;
}

export function DiagnosticResultListCard({
  result,
  index,
  isSelected,
  onSelect,
}: DiagnosticResultListCardProps) {
  const status = getResultStatusBadge(result);

  return (
    <article
      data-testid={`diagnostic-result-card-${result.orderCode}`}
      aria-current={isSelected ? 'true' : undefined}
      className={`space-y-3 rounded-xl border bg-white p-4 transition-colors ${
        isSelected ? 'border-primary ring-2 ring-primary/20' : 'border-border'
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-bold text-navy">
            {index}. {result.serviceName}
          </h3>
          <p className="mt-0.5 text-[11px] text-sub">
            <span className="font-mono">{result.orderCode}</span> • {result.serviceCode} • BS thực
            hiện {result.enteredByStaffName ?? '—'}
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap items-center justify-end gap-1">
          {isSelected ? (
            <Badge tone="sky">ĐANG XEM CHI TIẾT KQ</Badge>
          ) : (
            <Badge tone={status.tone}>
              {result.resultStatus === 'VERIFIED' ? (
                <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" />
              ) : null}
              {status.label}
            </Badge>
          )}
          {result.requiredForConclusion ? <Badge tone="rose">BẮT BUỘC</Badge> : null}
          {result.priority !== 'NORMAL' ? <Badge tone="amber">{result.priority}</Badge> : null}
        </div>
      </div>

      {result.isResultReady ? (
        <p className="text-xs text-ink">
          <span className="font-semibold text-navy">Tóm tắt kết luận: </span>
          {result.conclusion ?? 'Chưa có kết luận.'}
        </p>
      ) : (
        <p className="text-xs text-ink">
          Đang thực hiện tại {result.roomName ?? result.servicePointName ?? '—'} • Hàng đợi:{' '}
          {result.queueStatus ?? '—'}
        </p>
      )}

      {!isSelected ? (
        <button
          type="button"
          onClick={() => onSelect(result.orderId)}
          aria-label={`Xem chi tiết phiếu KQ ${result.serviceCode}`}
          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
        >
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Xem chi tiết phiếu KQ</span>
        </button>
      ) : null}
    </article>
  );
}
