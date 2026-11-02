import { useEffect, useRef, useState } from 'react';
import { Check, Copy } from 'lucide-react';

interface ConfirmationSuccessHeroProps {
  appointmentCode: string;
}

export function ConfirmationSuccessHero({ appointmentCode }: ConfirmationSuccessHeroProps) {
  const [copyFeedback, setCopyFeedback] = useState<'copied' | 'error' | null>(null);
  const timeoutRef = useRef<number | undefined>(undefined);

  useEffect(() => {
    return () => {
      window.clearTimeout(timeoutRef.current);
    };
  }, []);

  const handleCopyCode = async () => {
    window.clearTimeout(timeoutRef.current);

    if (typeof navigator === 'undefined' || !navigator.clipboard?.writeText) {
      setCopyFeedback('error');
      timeoutRef.current = window.setTimeout(() => setCopyFeedback(null), 2000);
      return;
    }

    try {
      await navigator.clipboard.writeText(`#${appointmentCode}`);
      setCopyFeedback('copied');
    } catch {
      setCopyFeedback('error');
    }

    timeoutRef.current = window.setTimeout(() => setCopyFeedback(null), 2000);
  };

  return (
    <section
      aria-label="Thông báo đặt lịch thành công"
      className="relative overflow-hidden rounded-2xl border border-border bg-white p-5 text-center shadow-xs sm:p-8"
    >
      <div
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-1.5 bg-linear-to-r from-emerald-400 via-sky-500 to-emerald-400"
      />

      <div className="mx-auto flex h-12 w-16 items-center justify-center rounded-full bg-emerald-50 ring-8 ring-emerald-50/50">
        <Check className="h-6 w-6 text-emerald-600" strokeWidth={2.5} aria-hidden="true" />
      </div>

      <h1 className="mt-4 text-2xl font-extrabold text-ink uppercase sm:text-3xl">
        ĐẶT LỊCH KHÁM NGOẠI TRÚ THÀNH CÔNG!
      </h1>

      <p className="mt-2 text-xs leading-relaxed text-sub sm:text-sm">
        Lịch khám của quý khách đã được hệ thống MedFlow xác nhận chính thức.
        <br />
        Số thứ tự giữ trước tại buồng khám đã sẵn sàng.
      </p>

      <div className="mt-5 inline-flex flex-wrap items-center justify-center gap-3 rounded-xl border border-sky-200 bg-sky-50 px-5 py-3">
        <span className="text-xs font-bold tracking-wider text-sub uppercase">MÃ LỊCH HẸN:</span>
        <span className="font-mono text-xl font-extrabold text-primary">#{appointmentCode}</span>
        <button
          type="button"
          onClick={handleCopyCode}
          aria-label="Sao chép mã lịch hẹn"
          className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-sky-200 bg-white text-primary transition-colors hover:bg-sky-100"
        >
          <Copy className="h-4 w-4" aria-hidden="true" />
        </button>
        {copyFeedback === 'copied' && (
          <span className="text-xs font-semibold text-emerald-600">Đã sao chép</span>
        )}
        {copyFeedback === 'error' && (
          <span className="text-xs font-semibold text-rose-600">Không thể sao chép</span>
        )}
      </div>
    </section>
  );
}
