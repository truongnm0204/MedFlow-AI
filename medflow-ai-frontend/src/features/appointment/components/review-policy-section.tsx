import { ShieldCheck } from 'lucide-react';

import type { BookingReviewSummary } from '../types/booking-review.types';

interface ReviewPolicySectionProps {
  summary: BookingReviewSummary;
}

export function ReviewPolicySection({ summary }: ReviewPolicySectionProps) {
  const shortRoom = summary.roomLabel.match(/Phòng\s+\d+/i)?.[0]?.toLowerCase() ?? 'buồng khám';

  return (
    <section
      aria-label="Chính sách tiếp đón và cấp số thứ tự giữ trước"
      className="rounded-2xl border border-sky-200 bg-sky-50/60 p-6"
    >
      <div className="flex items-center gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary text-white">
          <ShieldCheck className="h-5 w-5" aria-hidden="true" />
        </div>
        <h2 className="text-sm font-bold text-ink">
          Chính sách Tiếp đón &amp; Cấp Số Thứ Tự (STT) Giữ Trước
        </h2>
      </div>

      <ol className="mt-4 space-y-3 text-xs leading-relaxed text-ink">
        <li className="flex items-start gap-2.5">
          <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-100 text-[11px] font-bold text-primary">
            1
          </span>
          <p>
            Ngay sau khi bấm xác nhận, hệ thống sẽ tự động cấp <strong>Mã Lịch Hẹn điện tử</strong>{' '}
            và <strong>Số Thứ Tự (STT) giữ trước</strong> tại {shortRoom}.
          </p>
        </li>

        <li className="flex items-start gap-2.5">
          <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-100 text-[11px] font-bold text-primary">
            2
          </span>
          <p>
            Quý khách vui lòng có mặt tại <strong>Quầy Tiếp đón Ngoại trú (Tầng 1 - Nhà A)</strong>{' '}
            trước giờ khám tối thiểu <strong>15 phút ({summary.checkInDeadlineTime})</strong> để đối
            chiếu thông tin và nhận phiếu khám vào buồng.
          </p>
        </li>

        <li className="flex items-start gap-2.5">
          <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-100 text-[11px] font-bold text-primary">
            3
          </span>
          <p>
            Nếu đến trễ quá <strong>15 phút</strong> so với giờ hẹn, số thứ tự sẽ được chuyển sang
            hàng đợi điều phối lại để đảm bảo quyền lợi cho các bệnh nhân tiếp theo.
          </p>
        </li>
      </ol>
    </section>
  );
}
