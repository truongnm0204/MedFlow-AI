import { DoorClosed, ShieldCheck } from 'lucide-react';
import { useState } from 'react';

import type { DoctorDetailProfile } from '../types/doctor-detail.types';

interface DoctorDetailSummaryProps {
  doctor: DoctorDetailProfile;
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

export function DoctorDetailSummary({ doctor }: DoctorDetailSummaryProps) {
  const [failedAvatarUrl, setFailedAvatarUrl] = useState<string | null>(null);
  const showImage = isUsableAvatarUrl(doctor.avatarUrl) && failedAvatarUrl !== doctor.avatarUrl;

  const formattedPrice =
    doctor.price != null
      ? `${new Intl.NumberFormat('vi-VN').format(doctor.price)} VNĐ`
      : (doctor.priceFormatted ?? 'Chưa cập nhật');

  return (
    <div className="rounded-2xl border border-border bg-white p-4.5 shadow-2xs sm:p-5">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:gap-5">
        {/* Avatar with Verified Badge */}
        <div className="relative shrink-0 self-start">
          {showImage ? (
            <img
              src={doctor.avatarUrl}
              alt={doctor.name}
              className="h-20 w-20 rounded-2xl border border-sky-100 object-cover shadow-2xs sm:h-22 sm:w-22"
              onError={() => setFailedAvatarUrl(doctor.avatarUrl)}
            />
          ) : (
            <div
              aria-hidden="true"
              className="flex h-20 w-20 items-center justify-center rounded-2xl border border-sky-200 bg-sky-50 text-lg font-bold text-primary shadow-2xs select-none sm:h-22 sm:w-22"
            >
              {getDoctorInitials(doctor.name)}
            </div>
          )}
          <div
            className="absolute -right-1.5 -bottom-1.5 flex h-6 w-6 items-center justify-center rounded-full border-2 border-white bg-primary text-white shadow-xs"
            title="Bác sĩ đã được xác minh danh tính và CCHN Bộ Y Tế"
          >
            <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          </div>
        </div>

        {/* Doctor Info & Room */}
        <div className="min-w-0 flex-1 space-y-1.5">
          <h2 className="text-xl font-bold tracking-tight text-ink sm:text-2xl">{doctor.name}</h2>

          <p className="text-xs font-semibold text-primary sm:text-sm">{doctor.roleTitle}</p>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-1 text-xs text-sub">
            <div className="flex items-center gap-1.5">
              <DoorClosed className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
              <span className="font-medium text-ink">{doctor.room}</span>
            </div>

            <span className="text-slate-300" aria-hidden="true">
              •
            </span>

            <div className="flex items-center gap-1.5 font-medium text-emerald-700">
              <span className="h-2 w-2 rounded-full bg-emerald-500" aria-hidden="true" />
              <span>{doctor.statusText}</span>
            </div>
          </div>

          {/* Pricing Row */}
          <div className="flex items-baseline gap-2 pt-2">
            <span className="text-xs text-sub">Giá theo cơ sở/ngày khám</span>
            <span className="text-lg font-bold text-ink sm:text-xl">{formattedPrice}</span>
          </div>
        </div>
      </div>
    </div>
  );
}
