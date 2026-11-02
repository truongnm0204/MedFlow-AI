import { QrCode, Receipt } from 'lucide-react';

import type { ReviewFeeSummary } from '../types/review-booking.types';

interface ReviewFeeBreakdownCardProps {
  feeSummary: ReviewFeeSummary;
}

function formatVnd(amount: number): string {
  return `${new Intl.NumberFormat('vi-VN').format(amount)} VNĐ`;
}

export function ReviewFeeBreakdownCard({ feeSummary }: ReviewFeeBreakdownCardProps) {
  return (
    <section
      aria-label="3. Chi phí dịch vụ niêm yết"
      className="rounded-2xl border border-border bg-white p-6 shadow-xs sm:p-7"
    >
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <div className="flex items-center gap-2.5">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-sky-100 bg-sky-50 text-primary">
            <Receipt className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-base font-bold tracking-tight text-ink">
              3. CHI PHÍ DỊCH VỤ NIÊM YẾT
            </h2>
            <p className="text-xs text-sub">
              Minh bạch 100%, không phát sinh phụ phí ẩn ngoài danh mục
            </p>
          </div>
        </div>

        <span className="inline-flex items-center rounded-md border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-semibold text-amber-800">
          {feeSummary.serviceTypeBadge}
        </span>
      </div>

      {/* Line Items */}
      <div className="mt-5 space-y-3">
        {feeSummary.items.map((item) => (
          <div key={item.id} className="flex flex-wrap items-center justify-between gap-2 py-1">
            <div>
              <p className="text-sm font-medium text-ink">{item.title}</p>
              <p className="text-xs text-sub">{item.description}</p>
            </div>

            {item.isIncludedFree ? (
              <span className="inline-flex items-center rounded bg-emerald-50 px-2.5 py-0.5 text-sm font-semibold text-emerald-700">
                {item.freeBadgeLabel ?? '0 VNĐ (Đã bao gồm trọn gói)'}
              </span>
            ) : (
              <span className="text-base font-bold text-ink">{formatVnd(item.amount)}</span>
            )}
          </div>
        ))}

        <div className="border-t border-dashed border-slate-300" />

        {/* Total Due */}
        <div className="flex flex-wrap items-center justify-between gap-4 pt-1">
          <div>
            <p className="text-xs font-bold uppercase tracking-wider text-sub">
              TỔNG CHI PHÍ THĂM KHÁM
            </p>
            <p className="text-xs text-sub">{feeSummary.totalDisclaimer}</p>
          </div>

          <p className="text-2xl font-bold tracking-tight text-primary sm:text-3xl">
            {formatVnd(feeSummary.totalAmount)}
          </p>
        </div>

        {/* Flexible Payment Note */}
        <div className="mt-2 flex items-start gap-2.5 rounded-xl border border-border bg-canvas p-4 text-xs leading-relaxed text-sub">
          <QrCode className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <p>
            <strong className="font-bold text-ink">Phương thức thanh toán linh hoạt:</strong> Quý
            khách có thể quét mã <strong className="font-bold text-ink">VietQR động bảo mật</strong>{' '}
            ngay sau khi xác nhận trên hệ thống hoặc thanh toán trực tiếp bằng tiền mặt / quẹt thẻ
            POS tại Quầy Tiếp đón khi đến viện.
          </p>
        </div>
      </div>
    </section>
  );
}
