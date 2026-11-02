import { Info, Phone } from 'lucide-react';

import type { Hospital } from '../types/booking-entry.types';

interface BookingEntrySummaryProps {
  hospital: Hospital | null;
}

export function BookingEntrySummary({ hospital }: BookingEntrySummaryProps) {
  return (
    <div className="space-y-4">
      {/* Lựa chọn của bạn */}
      <div className="rounded-2xl border border-border bg-white p-4 shadow-sm">
        <h3 className="text-xs font-bold uppercase tracking-wider text-sub">Lựa chọn của bạn</h3>
        <dl className="mt-3 space-y-3 text-xs">
          <div>
            <dt className="font-semibold text-slate-500">Cơ sở khám</dt>
            {hospital ? (
              <dd className="mt-0.5">
                <p className="font-bold text-ink">{hospital.name}</p>
                <p className="mt-0.5 leading-snug text-sub">{hospital.address}</p>
              </dd>
            ) : (
              <dd className="mt-0.5 italic text-sub">Chưa chọn</dd>
            )}
          </div>
          <div>
            <dt className="font-semibold text-slate-500">Hình thức đặt khám</dt>
            <dd className="mt-0.5 italic text-sub">Chọn ở Bước 2</dd>
          </div>
        </dl>
      </div>

      {/* Lưu ý khi đặt khám */}
      <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4">
        <div className="flex items-center gap-2">
          <Info className="h-4 w-4 text-amber-600" aria-hidden="true" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-amber-700">
            Lưu ý khi đặt khám
          </h3>
        </div>
        <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-relaxed text-amber-800">
          <li>Vui lòng có mặt trước giờ hẹn 15 phút để làm thủ tục.</li>
          <li>Mang theo CCCD/BHYT khi đến khám.</li>
        </ul>
      </div>

      {/* Hotline */}
      <a
        href="tel:19006868"
        className="flex items-center gap-3 rounded-2xl border border-border bg-white p-4 shadow-sm transition-colors hover:border-sky-300"
      >
        <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-primary">
          <Phone className="h-5 w-5" aria-hidden="true" />
        </span>
        <span>
          <span className="block text-xs text-sub">Cần hỗ trợ đặt lịch?</span>
          <span className="block text-sm font-bold text-ink">Tổng đài 1900 6868</span>
        </span>
      </a>
    </div>
  );
}
