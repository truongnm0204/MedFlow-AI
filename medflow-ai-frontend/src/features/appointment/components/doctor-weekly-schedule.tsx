import { Calendar } from 'lucide-react';

import type { WeeklyScheduleDay } from '../types/doctor-detail.types';

interface DoctorWeeklyScheduleProps {
  schedule: WeeklyScheduleDay[];
}

export function DoctorWeeklySchedule({ schedule }: DoctorWeeklyScheduleProps) {
  return (
    <div className="space-y-3">
      {/* Section Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-slate-800">
          <Calendar className="h-4 w-4 text-sky-600" />
          <span>LỊCH TRỰC KHÁM TẠI VIỆN TRONG TUẦN</span>
        </div>

        <span className="text-xs font-medium text-sky-600 hover:underline cursor-pointer">
          Cập nhật theo thời gian thực
        </span>
      </div>

      {/* Grid 3 Columns */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        {schedule.map((item, index) => {
          const isActive = item.isActive;
          return (
            <div
              key={index}
              className={`flex flex-col justify-between rounded-xl p-3.5 transition-all sm:p-4 ${
                isActive
                  ? 'border-2 border-sky-400 bg-sky-50/30 shadow-2xs'
                  : 'border border-slate-200/90 bg-white hover:border-slate-300'
              }`}
            >
              <div>
                {/* Day Header */}
                <div className="flex items-center justify-between">
                  <span className="text-sm font-bold text-slate-900">{item.dayOfWeek}</span>
                  {item.hasAvailableSlot && (
                    <span
                      className="h-2 w-2 rounded-full bg-emerald-500"
                      title="Có ca khám trực tiếp"
                    />
                  )}
                </div>

                {/* Sub Note */}
                {item.note && <p className="mt-1 text-[11px] text-slate-500 italic">{item.note}</p>}

                {/* Time Slots */}
                <div className="mt-2 space-y-1 text-xs font-semibold text-slate-800">
                  {item.morningTime && <div>{item.morningTime}</div>}
                  {item.afternoonTime && <div>{item.afternoonTime}</div>}
                </div>
              </div>

              {/* Status Tag */}
              {item.statusTag && (
                <div className="mt-3">
                  <span className="inline-block rounded bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                    {item.statusTag}
                  </span>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
