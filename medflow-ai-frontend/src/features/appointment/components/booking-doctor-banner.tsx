import { ArrowLeftRight, DoorClosed, Stethoscope } from 'lucide-react';

import type { BookingDoctorServiceInfo } from '../types/slot-booking.types';

interface BookingDoctorBannerProps {
  info: BookingDoctorServiceInfo;
  onChangeDoctorOrService: () => void;
}

export function BookingDoctorBanner({ info, onChangeDoctorOrService }: BookingDoctorBannerProps) {
  const formattedPrice = new Intl.NumberFormat('vi-VN').format(info.price);

  return (
    <section
      aria-label="Thông tin bác sĩ và dịch vụ đã chọn"
      className="mb-6 flex flex-col justify-between gap-4 rounded-2xl border border-sky-200/80 bg-white p-5 shadow-xs lg:flex-row lg:items-center"
    >
      <div className="flex items-start gap-4">
        <div className="relative shrink-0">
          <img
            src={info.avatarUrl}
            alt={info.doctorName}
            className="h-16 w-16 rounded-2xl border border-border object-cover"
            onError={(e) => {
              (e.target as HTMLImageElement).src =
                'https://images.unsplash.com/photo-1622253692010-333f2da6031d?w=150&auto=format&fit=crop&q=80';
            }}
          />
          <span className="absolute -bottom-1.5 left-1/2 -translate-x-1/2 whitespace-nowrap rounded-full border border-white bg-emerald-500 px-2 py-0.5 text-[10px] font-bold text-white shadow-2xs">
            {info.availabilityLabel}
          </span>
        </div>

        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-base font-bold text-ink sm:text-lg">{info.doctorName}</h2>
            <span className="inline-flex items-center rounded-md bg-sky-50 px-2.5 py-0.5 text-[11px] font-semibold text-primary">
              {info.roleTag}
            </span>
          </div>

          <div className="flex items-center gap-1.5 text-xs text-sub">
            <DoorClosed className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
            <span>{info.room}</span>
          </div>

          <div className="flex items-center gap-1.5 text-xs text-sub">
            <Stethoscope className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
            <span>
              Dịch vụ: <strong className="font-semibold text-ink">{info.serviceName}</strong>{' '}
              <span className="font-mono text-xs font-semibold text-ink">
                (Mã: {info.serviceCode})
              </span>
            </span>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border pt-3 lg:border-l lg:border-t-0 lg:pl-6 lg:pt-0">
        <div>
          <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
            GIÁ DỊCH VỤ ĐANG HIỆU LỰC
          </p>
          <p className="mt-0.5 text-xl font-extrabold text-primary">
            {formattedPrice} <span className="text-xs font-bold">VNĐ</span>
          </p>
        </div>

        <button
          type="button"
          onClick={onChangeDoctorOrService}
          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-ink shadow-2xs transition-colors hover:bg-canvas focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <ArrowLeftRight className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          <span>Đổi bác sĩ / dịch vụ</span>
        </button>
      </div>
    </section>
  );
}
