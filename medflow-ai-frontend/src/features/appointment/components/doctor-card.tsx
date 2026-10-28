import { Clock, DoorClosed } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { useBookingStore } from '../store/booking.store';
import type { DoctorSearchResult, ScheduleBadgeStatus } from '../types/doctor-search.types';

interface DoctorCardProps {
  doctor: DoctorSearchResult;
  onViewProfile?: (doctor: DoctorSearchResult) => void;
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

  const formattedPrice = new Intl.NumberFormat('vi-VN').format(doctor.price);

  const handleBookNow = () => {
    selectDoctor(doctor.id);
    navigate(`/appointment/booking?doctorId=${doctor.id}`);
  };

  return (
    <div className="flex flex-col justify-between rounded-2xl border border-slate-200/90 bg-white p-5 shadow-sm transition-all hover:border-sky-300 hover:shadow-md">
      <div>
        {/* Top Profile Header */}
        <div className="flex items-start gap-3.5">
          <div className="relative shrink-0">
            <img
              src={doctor.avatarUrl}
              alt={doctor.name}
              className="h-14 w-14 rounded-xl border border-slate-200 object-cover"
              onError={(e) => {
                // Fallback avatar
                (e.target as HTMLImageElement).src =
                  'https://images.unsplash.com/photo-1622253692010-333f2da6031d?w=150&auto=format&fit=crop&q=80';
              }}
            />
            <span
              className="absolute -bottom-1 -right-1 block h-3.5 w-3.5 rounded-full border-2 border-white bg-emerald-500"
              title="Đang sẵn sàng nhận lịch"
            />
          </div>

          <div className="min-w-0 flex-1">
            <span className="inline-block rounded-md bg-sky-50/80 px-2.5 py-0.5 text-[11px] font-medium text-sky-800">
              {doctor.roleTag}
            </span>
            <h3 className="mt-1 truncate text-base font-bold text-slate-900" title={doctor.name}>
              {doctor.name}
            </h3>
            <p className="mt-0.5 text-xs text-slate-500">
              {doctor.specialty} • {doctor.yearsOfExperience} năm kinh nghiệm
            </p>
          </div>
        </div>

        {/* Room Info Box */}
        <div className="mt-3.5 flex items-center gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
          <DoorClosed className="h-4 w-4 shrink-0 text-slate-400" />
          <span className="truncate font-medium">{doctor.room}</span>
        </div>

        {/* Schedule Box */}
        <div className="mt-3 rounded-lg border border-slate-100 bg-slate-50/50 p-2.5">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 text-xs text-slate-600">
              <Clock className="h-3.5 w-3.5 text-sky-600" />
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
          <div className="mt-1 text-sm font-semibold text-slate-900">{doctor.nearestSchedule}</div>
        </div>
      </div>

      {/* Pricing and Actions */}
      <div className="mt-4 border-t border-slate-100 pt-3">
        <div className="flex items-baseline justify-between">
          <span className="text-xs text-slate-400">Giá theo bảng giá hiệu lực</span>
          <span className="text-base font-bold text-sky-600">{formattedPrice} VNĐ</span>
        </div>

        <div className="mt-3 grid grid-cols-2 gap-2.5">
          <Button
            type="button"
            variant="secondary"
            className="w-full text-xs font-medium text-slate-700 hover:bg-slate-50"
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
            className="w-full bg-sky-600 text-xs font-semibold text-white shadow-sm hover:bg-sky-700"
            onClick={handleBookNow}
          >
            Đặt khám ngay
          </Button>
        </div>
      </div>
    </div>
  );
}
