import { Camera, CheckCircle2, Download, Film, Microscope } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { DiagnosticResultDetailView } from '../types/diagnostic-result-view.types';

interface DiagnosticImagingViewerProps {
  detail: DiagnosticResultDetailView;
  onDownloadPacs?: () => void;
}

export function DiagnosticImagingViewer({ detail, onDownloadPacs }: DiagnosticImagingViewerProps) {
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
          <div className="flex items-center gap-2">
            <Camera className="h-4 w-4 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">
              Hình ảnh Nội soi NBI &amp; Thử nghiệm Vi sinh ({detail.images.length} ảnh chọn in)
            </h2>
          </div>
          <Badge tone="sky">{detail.deviceBadge}</Badge>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" data-testid="imaging-grid">
            {detail.images.map((img, idx) => (
              <figure
                key={img.id}
                className={`overflow-hidden rounded-xl border bg-white p-2.5 shadow-2xs ${
                  img.isAbnormal ? 'border-amber-300 bg-amber-50/30' : 'border-border'
                }`}
              >
                <div
                  role="img"
                  aria-label={img.label}
                  className="relative flex aspect-video items-center justify-center rounded-lg bg-gradient-to-br from-slate-900 via-slate-800 to-slate-700 text-xs font-mono text-white/75"
                >
                  <span>
                    IMG #{idx + 1} • {img.location.toUpperCase()}
                  </span>
                  {img.isAbnormal && (
                    <span className="absolute right-2 top-2 rounded bg-rose-600 px-1.5 py-0.5 text-[10px] font-bold text-white">
                      Tổn thương
                    </span>
                  )}
                </div>
                <figcaption className="mt-2 space-y-0.5">
                  <p className="text-xs font-bold text-navy">{img.label}</p>
                  <p className="text-[11px] leading-snug text-sub">{img.caption}</p>
                </figcaption>
              </figure>
            ))}
          </div>

          {/* Microbiology / Rapid Test */}
          <div className="rounded-xl border border-border bg-canvas p-4 space-y-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-xs font-bold text-navy">
                <Microscope className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>{detail.microbiologyTest.name}</span>
              </div>
              <Badge tone={detail.microbiologyTest.isPositive ? 'rose' : 'emerald'}>
                <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" />
                {detail.microbiologyTest.resultLabel}
              </Badge>
            </div>
            <p className="text-xs leading-relaxed text-ink">{detail.microbiologyTest.notes}</p>
          </div>

          {/* Video PACS attachment */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-slate-50 px-4 py-3 text-xs">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-100 text-sky-700">
                <Film className="h-4 w-4" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <p className="truncate font-mono font-bold text-navy">
                  {detail.videoAttachment.fileName}
                </p>
                <p className="truncate text-[11px] text-sub">
                  {detail.videoAttachment.durationLabel}
                </p>
              </div>
            </div>
            <button
              type="button"
              onClick={onDownloadPacs}
              aria-label="Tải tệp lưu trữ PACS"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 font-semibold text-navy transition-colors hover:bg-canvas"
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Tải PACS</span>
            </button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
