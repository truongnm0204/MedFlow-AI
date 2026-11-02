import { Clock, ShieldCheck } from 'lucide-react';

import type { ReviewReceptionPolicy } from '../types/review-booking.types';

interface ReviewReceptionPolicyCardProps {
  policy: ReviewReceptionPolicy;
}

export function ReviewReceptionPolicyCard({ policy }: ReviewReceptionPolicyCardProps) {
  return (
    <section
      aria-label="Chính sách tiếp đón và cấp số thứ tự giữ trước"
      className="rounded-2xl border border-sky-200 bg-sky-50/80 p-6"
    >
      <div className="flex items-start gap-3.5">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary text-white shadow-xs">
          <ShieldCheck className="h-5 w-5" aria-hidden="true" />
        </div>

        <div className="min-w-0 flex-1 space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-base font-bold text-navy">
              Chính sách Tiếp đón & Cấp Số Thứ Tự (STT) Giữ Trước
            </h3>

            <span className="inline-flex items-center gap-1 rounded-full border border-sky-200 bg-white/80 px-3 py-1 text-xs font-semibold text-sky-800">
              <Clock className="h-3 w-3" aria-hidden="true" />
              <span>{policy.badgeLabel}</span>
            </span>
          </div>

          <ol className="space-y-2.5 text-sm leading-relaxed text-ink">
            <li className="flex items-start gap-2">
              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-200/90 text-xs font-bold text-sky-950">
                1
              </span>
              <p>
                Ngay sau khi bấm xác nhận, hệ thống sẽ tự động cấp{' '}
                <strong className="font-bold">Mã Lịch Hẹn điện tử</strong> và{' '}
                <strong className="font-bold">Số Thứ Tự (STT)</strong> giữ trước tại buồng khám{' '}
                <span className="font-mono font-bold">{policy.roomNumber}</span>.
              </p>
            </li>

            <li className="flex items-start gap-2">
              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-200/90 text-xs font-bold text-sky-950">
                2
              </span>
              <p>
                Quý khách vui lòng có mặt tại{' '}
                <strong className="font-bold">{policy.receptionCounterLabel}</strong> trước giờ khám
                tối thiểu{' '}
                <strong className="font-bold">
                  {policy.earlyArrivalMinutes} phút ({policy.checkInDeadlineTime})
                </strong>{' '}
                để đối chiếu thông tin và nhận phiếu khám vào buồng.
              </p>
            </li>

            <li className="flex items-start gap-2">
              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-200/90 text-xs font-bold text-sky-950">
                3
              </span>
              <p>
                Nếu đến trễ quá {policy.lateGraceMinutes} phút so với giờ hẹn, số thứ tự sẽ được
                chuyển sang hàng đợi điều phối lại để đảm bảo quyền lợi cho các bệnh nhân tiếp theo.
              </p>
            </li>
          </ol>
        </div>
      </div>
    </section>
  );
}
