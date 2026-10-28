import { RotateCcw } from 'lucide-react';

import { ACADEMIC_DEGREES } from '../data/doctor-search-mock';

interface DoctorFilterBarProps {
  selectedDegree: string;
  hasScheduleToday: boolean;
  hasScheduleThisWeek: boolean;
  onSelectDegree: (degree: string) => void;
  onToggleScheduleToday: () => void;
  onToggleScheduleThisWeek: () => void;
  onResetFilters: () => void;
}

export function DoctorFilterBar({
  selectedDegree,
  hasScheduleToday,
  hasScheduleThisWeek,
  onSelectDegree,
  onToggleScheduleToday,
  onToggleScheduleThisWeek,
  onResetFilters,
}: DoctorFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 pt-1 text-xs">
      {/* Left Filters */}
      <div className="flex flex-wrap items-center gap-2.5">
        <div className="flex items-center gap-2">
          <span className="font-medium text-slate-500">Học hàm / Vị trí:</span>
          <select
            value={selectedDegree}
            onChange={(e) => onSelectDegree(e.target.value)}
            className="h-8 rounded-lg border border-slate-200 bg-white px-2.5 text-xs font-medium text-slate-700 shadow-sm focus:border-sky-500 focus:outline-none focus:ring-1 focus:ring-sky-500"
          >
            {ACADEMIC_DEGREES.map((deg) => (
              <option key={deg.id} value={deg.id}>
                {deg.name}
              </option>
            ))}
          </select>
        </div>

        {/* Toggle Có lịch hôm nay */}
        <button
          type="button"
          onClick={onToggleScheduleToday}
          className={`flex h-8 items-center gap-1.5 rounded-lg border px-3 text-xs font-medium transition-all ${
            hasScheduleToday
              ? 'border-emerald-500 bg-emerald-50 text-emerald-800'
              : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
          }`}
        >
          <span className="h-2 w-2 rounded-full bg-emerald-500" />
          <span>Có lịch hôm nay</span>
        </button>

        {/* Toggle Có lịch tuần này */}
        <button
          type="button"
          onClick={onToggleScheduleThisWeek}
          className={`flex h-8 items-center rounded-lg border px-3 text-xs font-medium transition-all ${
            hasScheduleThisWeek
              ? 'border-sky-500 bg-sky-50 text-sky-800'
              : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
          }`}
        >
          <span>Có lịch tuần này</span>
        </button>
      </div>

      {/* Right Reset Action */}
      <button
        type="button"
        onClick={onResetFilters}
        className="flex items-center gap-1.5 text-xs text-slate-400 hover:text-sky-600 transition-colors"
      >
        <RotateCcw className="h-3.5 w-3.5" />
        <span>Xóa tất cả bộ lọc</span>
      </button>
    </div>
  );
}
