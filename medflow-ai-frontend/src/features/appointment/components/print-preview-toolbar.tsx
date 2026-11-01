import { ArrowLeft, Download, Minus, Plus, Printer } from 'lucide-react';

import { Button } from '@/shared/ui/button';

export type PrintOrientation = 'portrait' | 'landscape';

interface PrintPreviewToolbarProps {
  orientation: PrintOrientation;
  zoom: number;
  onBack: () => void;
  onOrientationChange: (orientation: PrintOrientation) => void;
  onZoomChange: (zoom: number) => void;
  onPrint: () => void;
}

export function PrintPreviewToolbar({
  orientation,
  zoom,
  onBack,
  onOrientationChange,
  onZoomChange,
  onPrint,
}: PrintPreviewToolbarProps) {
  return (
    <header className="sticky top-0 z-20 border-b border-border bg-white px-6 py-3 shadow-xs print:hidden">
      <div className="mx-auto max-w-6xl space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="secondary" size="sm" type="button" onClick={onBack}>
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              <span>Quay lại</span>
            </Button>

            <div className="hidden h-5 w-px bg-border sm:block" aria-hidden="true" />

            <h1 className="text-sm font-extrabold text-ink sm:text-base">
              Xem trước Bản In Phiếu Hẹn Khám Ngoại Trú
            </h1>
          </div>

          <span className="inline-flex items-center rounded-lg border border-sky-200 bg-sky-50 px-2.5 py-1 text-[11px] font-bold text-primary">
            Mã màn hình: SCR-APT-07B • Khổ in tiêu chuẩn A5 y tế
          </span>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-3">
          <div className="flex flex-wrap items-center gap-3">
            <select
              aria-label="Khổ giấy"
              value={orientation}
              onChange={(event) => onOrientationChange(event.target.value as PrintOrientation)}
              className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-ink focus:border-primary focus:outline-none"
            >
              <option value="portrait">Khổ A5 Đứng (Portrait)</option>
              <option value="landscape">Khổ A5 Ngang (Landscape)</option>
            </select>

            <div className="inline-flex items-center gap-1 rounded-lg border border-border bg-canvas p-1">
              <button
                type="button"
                aria-label="Thu nhỏ"
                disabled={zoom <= 50}
                onClick={() => onZoomChange(Math.max(50, zoom - 10))}
                className="inline-flex h-6 w-6 items-center justify-center rounded-md text-ink transition-colors hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Minus className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
              <span className="min-w-24 text-center text-xs font-bold text-ink">
                {zoom === 100 ? '100% (Chuẩn)' : `${zoom}%`}
              </span>
              <button
                type="button"
                aria-label="Phóng to"
                disabled={zoom >= 150}
                onClick={() => onZoomChange(Math.min(150, zoom + 10))}
                className="inline-flex h-6 w-6 items-center justify-center rounded-md text-ink transition-colors hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Plus className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              type="button"
              title="Chọn 'Lưu dưới dạng PDF' trong hộp thoại in"
              onClick={onPrint}
            >
              <Download className="h-4 w-4" aria-hidden="true" />
              <span>Tải file PDF</span>
            </Button>

            <Button variant="primary" size="sm" type="button" onClick={onPrint}>
              <Printer className="h-4 w-4" aria-hidden="true" />
              <span>In Phiếu Ngay (Ctrl + P)</span>
            </Button>
          </div>
        </div>
      </div>
    </header>
  );
}
