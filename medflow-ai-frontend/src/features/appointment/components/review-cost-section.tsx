import { CreditCard, ScanQrCode } from 'lucide-react';

import type { BookingReviewSummary } from '../types/booking-review.types';

interface ReviewCostSectionProps {
  summary: BookingReviewSummary;
}

export function ReviewCostSection({ summary }: ReviewCostSectionProps) {
  const formattedPrice = `${new Intl.NumberFormat('vi-VN').format(summary.price)} VNĐ`;

  return (
    <section
      aria-label="Chi phí dịch vụ niêm yết"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <CreditCard className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-sm font-bold text-ink uppercase">3. CHI PHÍ DỊCH VỤ NIÊM YẾT</h2>
            <p className="text-xs text-sub">
              Minh bạch 100%, không phát sinh phụ phí ẩn ngoài danh mục
            </p>
          </div>
        </div>

        <span className="inline-flex items-center rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-bold text-amber-800">
          Dịch vụ Ngoại trú Tự nguyện
        </span>
      </div>

      <div className="mt-5 space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <p className="text-sm font-semibold text-ink">Phí khám {summary.serviceName}</p>
            <p className="text-xs text-sub">
              Khám lâm sàng chuyên sâu, chẩn đoán ban đầu &amp; tư vấn
            </p>
          </div>
          <p className="text-sm font-bold text-ink">{formattedPrice}</p>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <p className="text-sm font-semibold text-ink">
              Đo sinh hiệu ban đầu &amp; Lập hồ sơ y tế số
            </p>
            <p className="text-xs text-sub">
              Đo huyết áp, nhịp tim, SpO2, lập mã QR định danh hồ sơ
            </p>
          </div>
          <span className="inline-flex items-center rounded-full bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">
            0 VNĐ (Đã bao gồm trọn gói)
          </span>
        </div>
      </div>

      <div className="my-4 border-t border-dashed border-border" />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-extrabold text-ink uppercase">TỔNG CHI PHÍ THĂM KHÁM</p>
          <p className="text-xs text-sub">
            Chưa bao gồm xét nghiệm hoặc chẩn đoán hình ảnh chuyên sâu (nếu có chỉ định từ bác sĩ)
          </p>
        </div>
        <p className="text-3xl font-extrabold text-primary">{formattedPrice}</p>
      </div>

      <div className="mt-4 flex items-start gap-2.5 rounded-xl border border-border bg-canvas px-4 py-3 text-xs leading-relaxed text-sub">
        <ScanQrCode className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <p>
          Phương thức thanh toán linh hoạt: Quý khách có thể quét mã VietQR động bảo mật ngay sau
          khi xác nhận trên hệ thống hoặc thanh toán trực tiếp bằng tiền mặt / quẹt thẻ POS tại Quầy
          Tiếp đón khi đến viện.
        </p>
      </div>
    </section>
  );
}
