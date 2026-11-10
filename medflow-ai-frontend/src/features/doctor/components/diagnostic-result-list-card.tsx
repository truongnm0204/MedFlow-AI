import { CheckCircle2, Eye, FileSearch } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { DiagnosticLabFlag, DiagnosticResultItem } from '../types/review-diagnostic.types';

export interface DiagnosticResultListCardProps {
  result: DiagnosticResultItem;
  isSelected: boolean;
  onSelect: (resultId: string) => void;
}

const FLAG_META: Record<DiagnosticLabFlag, { label: string; className: string }> = {
  NORMAL: { label: 'Bình thường', className: 'text-emerald-700' },
  HIGH: { label: 'Cao', className: 'text-rose-700 font-bold' },
  LOW: { label: 'Thấp', className: 'text-amber-700 font-bold' },
};

export function DiagnosticResultListCard({
  result,
  isSelected,
  onSelect,
}: DiagnosticResultListCardProps) {
  const isViewing = isSelected;

  return (
    <article
      data-testid={`diagnostic-result-card-${result.serviceCode}`}
      aria-current={isSelected ? 'true' : undefined}
      className={`rounded-xl border bg-white p-4 space-y-3 transition-colors ${
        isSelected ? 'border-primary ring-2 ring-primary/20' : 'border-border'
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-bold text-navy">
            {result.orderIndex}. {result.serviceName}
          </h3>
          <p className="mt-0.5 text-[11px] text-sub">
            <span className="font-mono">{result.serviceCode}</span> • BS thực hiện{' '}
            <span className="font-semibold text-ink">{result.performedBy}</span>
          </p>
        </div>
        {isViewing ? (
          <Badge tone="sky" className="shrink-0">
            ĐANG XEM CHI TIẾT KQ
          </Badge>
        ) : (
          <Badge tone="emerald" className="shrink-0">
            <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" />
            ĐÃ KÝ SỐ DUYỆT
          </Badge>
        )}
      </div>

      {result.labIndicators.length > 0 ? (
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="border-b border-border text-sub">
              <th className="py-1 font-semibold">Chỉ số</th>
              <th className="py-1 font-semibold">Kết quả</th>
              <th className="py-1 font-semibold">Tham chiếu</th>
              <th className="py-1 font-semibold">Đánh giá</th>
            </tr>
          </thead>
          <tbody>
            {result.labIndicators.map((indicator) => (
              <tr key={indicator.code} className="border-b border-border/60 last:border-0">
                <td className="py-1 font-semibold text-navy">{indicator.code}</td>
                <td className="py-1 text-ink">
                  {indicator.value} {indicator.unit}
                </td>
                <td className="py-1 text-sub">{indicator.referenceRange}</td>
                <td className={`py-1 ${FLAG_META[indicator.flag].className}`}>
                  {FLAG_META[indicator.flag].label}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="space-y-2">
          <p className="text-xs text-ink">
            <span className="font-semibold text-navy">Tóm tắt kết luận: </span>
            {result.conclusionSummary}
          </p>
          {result.alertTag ? (
            <Badge tone={result.alertTag.tone}>{result.alertTag.label}</Badge>
          ) : null}
        </div>
      )}

      {!isViewing ? (
        <button
          type="button"
          onClick={() => onSelect(result.id)}
          aria-label={`Xem chi tiết phiếu KQ ${result.serviceCode}`}
          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
        >
          {result.labIndicators.length > 0 ? (
            <FileSearch className="h-3.5 w-3.5" aria-hidden="true" />
          ) : (
            <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          )}
          <span>Xem chi tiết phiếu KQ</span>
        </button>
      ) : null}
    </article>
  );
}
