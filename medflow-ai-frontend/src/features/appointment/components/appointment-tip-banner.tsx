import { FileDown, Lightbulb } from 'lucide-react';

interface AppointmentTipBannerProps {
  onDownloadGuidePdf?: () => void;
}

export function AppointmentTipBanner({ onDownloadGuidePdf }: AppointmentTipBannerProps) {
  return (
    <section
      aria-label="Mẹo tiếp đón nhanh không chờ đợi"
      className="mt-6 flex flex-col justify-between gap-4 rounded-2xl border border-sky-100 bg-sky-50/50 p-4 sm:flex-row sm:items-center"
    >
      <div className="flex items-start gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary text-white shadow-2xs">
          <Lightbulb className="h-4 w-4" aria-hidden="true" />
        </div>
        <div>
          <h3 className="text-xs font-bold text-ink">Mẹo tiếp đón nhanh không chờ đợi</h3>
          <p className="mt-0.5 text-xs leading-relaxed text-sub">
            Bạn có thể lưu mã QR phiếu hẹn về điện thoại hoặc xuất trình mã hẹn tại Quầy Tiếp đón
            Tầng 1 để nhận diện và in phiếu vào buồng khám ngay khi đến bệnh viện.
          </p>
        </div>
      </div>

      <button
        type="button"
        onClick={() => (onDownloadGuidePdf ? onDownloadGuidePdf() : window.print())}
        className="inline-flex shrink-0 items-center gap-1.5 self-start rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-bold text-ink shadow-2xs transition-colors hover:bg-canvas sm:self-center"
      >
        <FileDown className="h-3.5 w-3.5 text-rose-600" aria-hidden="true" />
        <span>Tải hướng dẫn đi khám PDF</span>
      </button>
    </section>
  );
}
