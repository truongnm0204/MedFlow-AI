import { Search, X } from 'lucide-react';

import type { SearchFilterTab } from '../types/patient-search.types';

interface PatientSearchFilterBarProps {
  query: string;
  onQueryChange: (query: string) => void;
  activeTab: SearchFilterTab;
  onTabChange: (tab: SearchFilterTab) => void;
  counts: {
    all: number;
    hasAppointment: number;
    checkedIn: number;
    noAppointment: number;
  };
}

export function PatientSearchFilterBar({
  query,
  onQueryChange,
  activeTab,
  onTabChange,
  counts,
}: PatientSearchFilterBarProps) {
  const tabs: Array<{ id: SearchFilterTab; label: string; count: number }> = [
    { id: 'ALL', label: 'Tất cả hồ sơ', count: counts.all },
    { id: 'HAS_APPOINTMENT', label: 'Có hẹn hôm nay', count: counts.hasAppointment },
    { id: 'CHECKED_IN', label: 'Đã check-in', count: counts.checkedIn },
    { id: 'NO_APPOINTMENT', label: 'Chưa có lịch hẹn', count: counts.noAppointment },
  ];

  return (
    <div className="space-y-4">
      {/* Search Input Bar */}
      <div className="relative flex items-center">
        <div className="absolute left-4 pointer-events-none text-slate-400">
          <Search className="w-5 h-5" aria-hidden="true" />
        </div>
        <input
          type="text"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          placeholder="Tra cứu theo Mã BN (BN-...), Số CCCD, Số điện thoại hoặc Họ tên bệnh nhân..."
          className="w-full h-12 pl-12 pr-28 rounded-2xl border border-slate-200 bg-white text-sm text-slate-900 placeholder:text-slate-400 focus:outline-hidden focus:ring-2 focus:ring-primary focus:border-transparent shadow-2xs transition-all"
        />
        {query && (
          <button
            type="button"
            onClick={() => onQueryChange('')}
            aria-label="Xóa từ khóa tìm kiếm"
            className="absolute right-12 p-1.5 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors"
          >
            <X className="w-4 h-4" aria-hidden="true" />
          </button>
        )}
        <div className="absolute right-3.5 hidden sm:flex items-center gap-1 px-2 py-0.5 rounded-md bg-slate-100 border border-slate-200 text-[10px] font-mono text-slate-500 select-none">
          <span>Enter</span>
        </div>
      </div>

      {/* Filter Tabs */}
      <div className="flex flex-wrap items-center gap-2" role="tablist" aria-label="Bộ lọc hồ sơ">
        {tabs.map((tab) => {
          const isActive = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => onTabChange(tab.id)}
              className={`inline-flex items-center gap-2 px-3.5 py-1.5 rounded-xl text-xs font-semibold transition-all ${
                isActive
                  ? 'bg-primary text-white shadow-xs'
                  : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50 hover:border-slate-300'
              }`}
            >
              <span>{tab.label}</span>
              <span
                className={`px-1.5 py-0.5 rounded-full font-mono text-[10px] font-bold ${
                  isActive ? 'bg-white/20 text-white' : 'bg-slate-100 text-slate-600'
                }`}
              >
                {tab.count}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
