import { DoorClosed, ShieldCheck } from 'lucide-react';

import type { DoctorDetailProfile } from '../types/doctor-detail.types';

interface DoctorDetailSummaryProps {
  doctor: DoctorDetailProfile;
}

export function DoctorDetailSummary({ doctor }: DoctorDetailSummaryProps) {
  const formattedPrice = new Intl.NumberFormat('vi-VN').format(doctor.price);

  return (
    <div className="rounded-2xl border border-slate-200/90 bg-white p-4.5 shadow-2xs sm:p-5">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:gap-5">
        {/* Avatar with Verified Badge */}
        <div className="relative shrink-0 self-start">
          <img
            src={doctor.avatarUrl}
            alt={doctor.name}
            className="h-20 w-20 rounded-2xl border border-sky-100 object-cover shadow-2xs sm:h-22 sm:w-22"
            onError={(e) => {
              (e.target as HTMLImageElement).src =
                'https://images.unsplash.com/photo-1622253692010-333f2da6031d?w=150&auto=format&fit=crop&q=80';
            }}
          />
          <div
            className="absolute -bottom-1.5 -right-1.5 flex h-6 w-6 items-center justify-center rounded-full border-2 border-white bg-sky-600 text-white shadow-xs"
            title="Bác sĩ đã được xác minh danh tính và CCHN Bộ Y Tế"
          >
            <ShieldCheck className="h-3.5 w-3.5" />
          </div>
        </div>

        {/* Doctor Info & Room */}
        <div className="min-w-0 flex-1 space-y-1.5">
          <h2 className="text-xl font-bold tracking-tight text-slate-900 sm:text-2xl">
            {doctor.name}
          </h2>

          <p className="text-xs font-semibold text-sky-700 sm:text-sm">{doctor.roleTitle}</p>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-1 text-xs text-slate-600">
            <div className="flex items-center gap-1.5">
              <DoorClosed className="h-3.5 w-3.5 text-slate-400" />
              <span className="font-medium">{doctor.room}</span>
            </div>

            <span className="text-slate-300">•</span>

            <div className="flex items-center gap-1.5 font-medium text-emerald-700">
              <span className="h-2 w-2 rounded-full bg-emerald-500" />
              <span>{doctor.statusText}</span>
            </div>
          </div>

          {/* Pricing Row */}
          <div className="pt-2 flex items-baseline gap-2">
            <span className="text-xs text-slate-500">Giá theo cơ sở/ngày khám</span>
            <span className="text-lg font-bold text-slate-900 sm:text-xl">
              {formattedPrice} VNĐ
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
