import { FileDown, Lightbulb, Share2 } from 'lucide-react';

import type { DiagnosticResultItem } from '../types/review-diagnostic.types';

export interface DiagnosticResultDetailPanelProps {
  result: DiagnosticResultItem;
  onDownloadPdf?: (result: DiagnosticResultItem) => void;
  onSharePortal?: (result: DiagnosticResultItem) => void;
}

export function DiagnosticResultDetailPanel({
  result,
  onDownloadPdf,
  onSharePortal,
}: DiagnosticResultDetailPanelProps) {
  return (
    <section
      aria-label="Phiếu kết quả CLS"
      data-testid="diagnostic-result-detail-panel"
      className="overflow-hidden rounded-xl border border-border bg-white"
    >
      <header className="bg-navy px-5 py-4 text-white">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-white/70">
          {result.serviceCode} • Ký số lúc {result.signedAtLabel}
        </p>
        <h3 className="mt-1 text-base font-bold">{result.serviceName}</h3>
        <p className="mt-1 text-xs text-white/80">BS thực hiện: {result.performedBy}</p>
      </header>

      <div className="space-y-5 p-5">
        <div>
          <h4 className="text-xs font-bold uppercase tracking-wide text-navy">Kết luận</h4>
          <p className="mt-1 text-sm text-ink">{result.conclusionSummary}</p>
        </div>

        {result.imageFrames.length > 0 ? (
          <div className="grid grid-cols-2 gap-3" data-testid="diagnostic-image-grid">
            {result.imageFrames.map((frame) => (
              <figure key={frame.id} className="space-y-1">
                <div
                  role="img"
                  aria-label={frame.caption}
                  className="aspect-video rounded-lg bg-gradient-to-br from-slate-800 to-slate-600"
                />
                <figcaption className="text-[11px] text-sub">{frame.caption}</figcaption>
              </figure>
            ))}
          </div>
        ) : null}

        <div>
          <h4 className="text-xs font-bold uppercase tracking-wide text-navy">
            MÔ TẢ GIẢI PHẪU CHI TIẾT
          </h4>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-ink">
            {result.anatomicalDescription.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </div>

        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
          <h4 className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wide text-amber-800">
            <Lightbulb className="h-4 w-4" aria-hidden="true" />
            ĐỀ NGHỊ CHUYÊN MÔN
          </h4>
          <p className="mt-1 text-sm text-amber-900">{result.recommendation}</p>
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => onDownloadPdf?.(result)}
            aria-label="Tải file PDF kết quả gốc"
            className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
          >
            <FileDown className="h-4 w-4" aria-hidden="true" />
            <span>Tải file PDF kết quả gốc</span>
          </button>
          <button
            type="button"
            onClick={() => onSharePortal?.(result)}
            aria-label="Chia sẻ portal bệnh nhân"
            className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
          >
            <Share2 className="h-4 w-4" aria-hidden="true" />
            <span>Chia sẻ portal bệnh nhân</span>
          </button>
        </div>
      </div>
    </section>
  );
}
