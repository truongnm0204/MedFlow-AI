import { ArrowLeftRight, DoorClosed, Stethoscope } from 'lucide-react';
import { useState } from 'react';

import type { BookingDoctorServiceInfo } from '../types/slot-booking.types';

interface BookingDoctorBannerProps {
  info: BookingDoctorServiceInfo;
  onChangeDoctorOrService: () => void;
}

function getDoctorInitials(name: string): string {
  const cleaned = name.replace(/^(PGS\.|GS\.|TS\.|ThS\.|BS\.|CKII|CKI|\s)+/gi, '').trim();
  const parts = (cleaned || name.trim()).split(/\s+/).filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0][0] ?? ''}${parts[parts.length - 1][0] ?? ''}`.toUpperCase();
  }
  return (parts[0]?.slice(0, 2) ?? 'BS').toUpperCase();
}

function isUsableAvatarUrl(url?: string | null): url is string {
  if (!url) return false;
  const trimmed = url.trim();
  return trimmed.length > 0 && !trimmed.includes('images.unsplash.com');
}

export function BookingDoctorBanner({ info, onChangeDoctorOrService }: BookingDoctorBannerProps) {
  const [failedAvatarUrl, setFailedAvatarUrl] = useState<string | null>(null);
  const formattedPrice = new Intl.NumberFormat('vi-VN').format(info.price);
  const showImage = isUsableAvatarUrl(info.avatarUrl) && failedAvatarUrl !== info.avatarUrl;

  return (
    <section
      aria-label="Thông tin bác sĩ và dịch vụ đã chọn"
      className="mb-6 flex flex-col justify-between gap-4 rounded-2xl border border-sky-200/80 bg-white p-5 shadow-xs lg:flex-row lg:items-center"
    >
      <div className="flex items-start gap-4">
        <div className="relative shrink-0">
          {showImage ? (
            <img
              src={info.avatarUrl}
              alt={info.doctorName}
              className="h-16 w-16 rounded-2xl border border-border object-cover"
              onError={() => setFailedAvatarUrl(info.avatarUrl)}
            />
          ) : (
            <div
              aria-hidden="true"
              className="flex h-16 w-16 items-center justify-center rounded-2xl border border-sky-200 bg-sky-50 text-base font-bold text-primary select-none"
            >
              {getDoctorInitials(info.doctorName)}
            </div>
          )}
          <span className="absolute -bottom-1.5 left-1/2 -translate-x-1/2 rounded-full border border-white bg-emerald-500 px-2 py-0.5 text-[10px] font-bold whitespace-nowrap text-white shadow-2xs">
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

      <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border pt-3 lg:border-t-0 lg:border-l lg:pt-0 lg:pl-6">
        <div>
          <p className="text-[10px] font-bold tracking-wider text-sub uppercase">
            GIÁ DỊCH VỤ ĐANG HIỆU LỰC
          </p>
          <p className="mt-0.5 text-xl font-extrabold text-primary">
            {formattedPrice} <span className="text-xs font-bold">VNĐ</span>
          </p>
        </div>

        <button
          type="button"
          onClick={onChangeDoctorOrService}
          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-ink shadow-2xs transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
        >
          <ArrowLeftRight className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          <span>Đổi bác sĩ / dịch vụ</span>
        </button>
      </div>
    </section>
  );
}
