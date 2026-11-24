import { Clock, DoorClosed } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { useBookingStore } from '../store/booking.store';
import type { DoctorSearchResult, ScheduleBadgeStatus } from '../types/doctor-search.types';

interface DoctorCardProps {
  doctor: DoctorSearchResult;
  onViewProfile?: (doctor: DoctorSearchResult) => void;
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

function getBadgeStyle(status: ScheduleBadgeStatus): string {
  switch (status) {
    case 'AVAILABLE':
      return 'bg-emerald-50 text-emerald-700 border-emerald-200';
    case 'FEW_SLOTS':
      return 'bg-amber-50 text-amber-800 border-amber-300 font-semibold';
    case 'ADVANCE_24H':
      return 'bg-sky-50 text-sky-700 border-sky-200';
    case 'ALMOST_FULL':
      return 'bg-orange-50 text-orange-800 border-orange-300 font-semibold';
    case 'WEEKEND':
      return 'bg-slate-100 text-slate-700 border-slate-200';
    default:
      return 'bg-slate-100 text-slate-700 border-slate-200';
  }
}

export function DoctorCard({ doctor, onViewProfile }: DoctorCardProps) {
  const navigate = useNavigate();
  const selectDoctor = useBookingStore((s) => s.selectDoctor);
  const [failedAvatarUrl, setFailedAvatarUrl] = useState<string | null>(null);

  const showImage = isUsableAvatarUrl(doctor.avatarUrl) && failedAvatarUrl !== doctor.avatarUrl;

  const formattedPrice =
    doctor.price != null
      ? `${new Intl.NumberFormat('vi-VN').format(doctor.price)} VNĐ`
      : (doctor.priceFormatted ?? 'Chưa cập nhật');

  const handleBookNow = () => {
    selectDoctor(doctor.id);
    navigate(`/appointment/doctors/${doctor.id}/booking`);
  };

  return (
    <div className="flex flex-col justify-between rounded-2xl border border-border bg-white p-5 shadow-sm transition-all hover:border-sky-300 hover:shadow-md">
      <div>
        {/* Top Profile Header */}
        <div className="flex items-start gap-3.5">
          <div className="relative shrink-0">
            {showImage ? (
              <img
                src={doctor.avatarUrl}
                alt={doctor.name}
                className="h-14 w-14 rounded-xl border border-border object-cover"
                onError={() => setFailedAvatarUrl(doctor.avatarUrl)}
              />
            ) : (
              <div
                aria-hidden="true"
                className="flex h-14 w-14 items-center justify-center rounded-xl border border-sky-200 bg-sky-50 text-sm font-bold text-primary select-none"
              >
                {getDoctorInitials(doctor.name)}
              </div>
            )}
            <span
              aria-hidden="true"
              className="absolute -right-1 -bottom-1 block h-3.5 w-3.5 rounded-full border-2 border-white bg-emerald-500"
            />
          </div>

          <div className="min-w-0 flex-1">
            <span className="inline-block rounded-md bg-sky-50 px-2.5 py-0.5 text-[11px] font-medium text-primary">
              {doctor.roleTag}
            </span>
            <h3 className="mt-1 truncate text-base font-bold text-ink" title={doctor.name}>
              {doctor.name}
            </h3>
            <p className="mt-0.5 text-xs text-sub">
              {doctor.specialty} • {doctor.yearsOfExperience} năm kinh nghiệm
            </p>
          </div>
        </div>

        {/* Room Info Box */}
        <div className="mt-3.5 flex items-center gap-2 rounded-lg bg-canvas px-3 py-2 text-xs text-sub">
          <DoorClosed className="h-4 w-4 shrink-0 text-sub" aria-hidden="true" />
          <span className="truncate font-medium text-ink">{doctor.room}</span>
        </div>

        {/* Schedule Box */}
        <div className="mt-3 rounded-lg border border-border bg-canvas/60 p-2.5">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 text-xs text-sub">
              <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Lịch khám gần nhất:</span>
            </div>
            <span
              className={`rounded border px-2 py-0.5 text-[11px] font-medium ${getBadgeStyle(
                doctor.scheduleStatus,
              )}`}
            >
              {doctor.scheduleStatusText}
            </span>
          </div>
          <div className="mt-1 text-sm font-semibold text-ink">{doctor.nearestSchedule}</div>
        </div>
      </div>

      {/* Pricing and Actions */}
      <div className="mt-4 border-t border-border pt-3">
        <div className="flex items-baseline justify-between">
          <span className="text-xs text-sub">Giá theo bảng giá hiệu lực</span>
          <span className="text-base font-bold text-primary">{formattedPrice}</span>
        </div>

        <div className="mt-3 grid grid-cols-2 gap-2.5">
          <Button
            type="button"
            variant="secondary"
            className="w-full text-xs font-medium text-ink hover:bg-canvas"
            onClick={() => {
              if (onViewProfile) {
                onViewProfile(doctor);
              } else {
                navigate(`/doctors/${doctor.id}`);
              }
            }}
          >
            Xem hồ sơ
          </Button>

          <Button
            type="button"
            className="w-full bg-primary text-xs font-semibold text-white shadow-sm hover:bg-primary-dark"
            onClick={handleBookNow}
          >
            Đặt khám ngay
          </Button>
        </div>
      </div>
    </div>
  );
}
