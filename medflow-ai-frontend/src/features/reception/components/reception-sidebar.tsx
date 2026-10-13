import { useState } from 'react';

import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

interface ReceptionSidebarProps {
  activeItem?: string;
  onItemSelect?: (item: string) => void;
}

const NAV_ITEMS = [
  {
    id: 'dashboard',
    label: 'Tổng quan quầy tiếp đón',
    icon: 'M4 5a1 1 0 011-1h14a1 1 0 011 1v2a1 1 0 01-1 1H5a1 1 0 01-1-1V5zM4 13a1 1 0 011-1h6a1 1 0 011 1v6a1 1 0 01-1 1H5a1 1 0 01-1-1v-6zM16 13a1 1 0 011-1h2a1 1 0 011 1v6a1 1 0 01-1 1h-2a1 1 0 01-1-1v-6z',
  },
  {
    id: 'check-in',
    label: 'Check-in & Tiếp nhận',
    badge: '8 chờ',
    icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4',
  },
  {
    id: 'queue',
    label: 'Hàng đợi khám',
    badge: '14',
    icon: 'M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10',
  },
  {
    id: 'patients',
    label: 'Hồ sơ bệnh nhân',
    icon: 'M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z',
  },
  {
    id: 'appointments',
    label: 'Lịch hẹn hôm nay',
    icon: 'M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z',
  },
];

export function ReceptionSidebar({
  activeItem = 'dashboard',
  onItemSelect,
}: ReceptionSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);

  return (
    <aside
      className={`bg-white border-r border-slate-200 flex flex-col transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      }`}
    >
      {/* Brand + collapse */}
      <div className="h-16 px-4 border-b border-slate-200 flex items-center justify-between">
        <div className="flex items-center gap-2.5 overflow-hidden">
          <MedflowLogo
            variant={isCollapsed ? 'icon' : 'full'}
            className={isCollapsed ? 'h-8 w-auto flex-shrink-0' : 'h-9 w-auto flex-shrink-0'}
            linkToHome={false}
          />
          {!isCollapsed && (
            <div className="text-[9px] font-mono text-slate-400 uppercase truncate">
              Quầy tiếp đón • CS1
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={() => setIsCollapsed(!isCollapsed)}
          className="p-1 rounded-md text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors"
          title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth="2"
              d={isCollapsed ? 'M13 5l7 7-7 7M5 5l7 7-7 7' : 'M11 19l-7-7 7-7m8 14l-7-7 7-7'}
            />
          </svg>
        </button>
      </div>

      {/* Role identity */}
      {!isCollapsed && (
        <div className="m-3 p-3 rounded-xl bg-emerald-50 border border-emerald-100 flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
            LT
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[9px] font-mono uppercase text-emerald-700 font-bold tracking-wider">
              Reception Desk
            </div>
            <div className="text-xs font-bold text-slate-800 truncate">Nhân viên tiếp đón</div>
            <div className="text-[10px] text-slate-500">Quầy số 1 • Sảnh A</div>
          </div>
          <span className="w-2 h-2 rounded-full bg-emerald-500 ring-2 ring-emerald-200" />
        </div>
      )}

      {/* Nav */}
      <nav className="px-3 py-2 space-y-1 flex-1">
        {!isCollapsed && (
          <div className="px-2 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
            Nghiệp vụ tiếp đón
          </div>
        )}
        {NAV_ITEMS.map((item) => {
          const isActive = activeItem === item.id;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => onItemSelect?.(item.id)}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-semibold transition-all ${
                isActive
                  ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/30'
                  : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
              }`}
              title={item.label}
            >
              <div className="flex items-center gap-2.5 min-w-0">
                <svg
                  className={`w-4 h-4 flex-shrink-0 ${isActive ? 'text-white' : 'text-slate-400'}`}
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth="2"
                    d={item.icon}
                  />
                </svg>
                {!isCollapsed && <span className="truncate">{item.label}</span>}
              </div>
              {!isCollapsed && item.badge && (
                <span
                  className={`px-1.5 py-0.5 rounded-full text-[10px] font-mono font-bold ${
                    isActive
                      ? 'bg-white/20 text-white'
                      : 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                  }`}
                >
                  {item.badge}
                </span>
              )}
            </button>
          );
        })}
      </nav>

      {/* Footer */}
      <div className="p-3 bg-slate-50 border-t border-slate-200">
        {!isCollapsed ? (
          <div className="p-2 rounded-lg bg-white border border-slate-200 text-[10px] space-y-1 shadow-2xs">
            <div className="flex items-center justify-between text-slate-700 font-semibold">
              <span>HIS SYNC: ONLINE</span>
              <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
            </div>
            <div className="flex items-center justify-between font-mono text-slate-400">
              <span>Ca sáng 07:30–11:30</span>
              <span className="text-emerald-600 font-semibold">#104</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center" title="HIS Sync Online">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 ring-2 ring-emerald-200 animate-pulse" />
          </div>
        )}
      </div>
    </aside>
  );
}
