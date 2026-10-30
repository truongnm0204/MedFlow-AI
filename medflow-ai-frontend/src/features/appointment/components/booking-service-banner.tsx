import { ArrowLeftRight, BriefcaseMedical, Clock, MapPin } from 'lucide-react';

import type { BookingServiceOnlyInfo } from '../types/slot-booking.types';

interface BookingServiceBannerProps {
  serviceInfo: BookingServiceOnlyInfo;
  onChangeService: () => void;
}

export function BookingServiceBanner({ serviceInfo, onChangeService }: BookingServiceBannerProps) {
  const formattedPrice = new Intl.NumberFormat('vi-VN').format(serviceInfo.price);

  return (
    <section
      aria-label="Thông tin dịch vụ khám đã chọn"
      className="mb-6 flex flex-col justify-between gap-4 rounded-2xl border border-sky-200/80 bg-white p-5 shadow-xs lg:flex-row lg:items-center"
    >
      <div className="flex items-start gap-4">
        <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border border-sky-100 bg-sky-50 text-primary shadow-2xs">
          <BriefcaseMedical className="h-7 w-7" aria-hidden="true" />
        </div>

        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs font-bold text-primary">
              {serviceInfo.serviceCode}
            </span>
            <span className="text-xs text-sub">• {serviceInfo.departmentName}</span>
            <span className="inline-flex items-center rounded-full bg-emerald-50 px-2.5 py-0.5 text-[11px] font-semibold text-emerald-700">
              {serviceInfo.onlineBadgeLabel}
            </span>
          </div>

          <h2 className="text-base font-bold text-ink sm:text-lg">{serviceInfo.serviceName}</h2>

          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
            <span className="inline-flex items-center gap-1.5">
              <MapPin className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
              <span>{serviceInfo.location}</span>
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
              <span>{serviceInfo.durationDescription}</span>
            </span>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border pt-3 lg:border-l lg:border-t-0 lg:pl-6 lg:pt-0">
        <div>
          <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
            GIÁ DỊCH VỤ NIÊM YẾT
          </p>
          <p className="mt-0.5 text-xl font-extrabold text-primary">
            {formattedPrice} <span className="text-xs font-bold">VNĐ</span>
          </p>
        </div>

        <button
          type="button"
          onClick={onChangeService}
          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-ink shadow-2xs transition-colors hover:bg-canvas focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <ArrowLeftRight className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          <span>Đổi dịch vụ khám</span>
        </button>
      </div>
    </section>
  );
}
