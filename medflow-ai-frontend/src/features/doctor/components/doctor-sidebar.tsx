import { useState } from 'react';

import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

interface DoctorSidebarProps {
  activeItem?: string;
  doctorName?: string;
  roomLabel?: string;
  onItemSelect?: (item: string) => void;
}

const DOCTOR_NAV_ITEMS = [
  {
    id: 'dashboard',
    label: 'Tổng quan bàn khám',
    icon: 'M4 5a1 1 0 011-1h14a1 1 0 011 1v2a1 1 0 01-1 1H5a1 1 0 01-1-1V5zM4 13a1 1 0 011-1h6a1 1 0 011 1v6a1 1 0 01-1 1H5a1 1 0 01-1-1v-6zM16 13a1 1 0 011-1h2a1 1 0 011 1v6a1 1 0 01-1 1h-2a1 1 0 01-1-1v-6z',
  },
  {
    id: 'assigned-queue',
    label: 'Hàng đợi phân công',
    badge: '9 chờ',
    icon: 'M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10',
  },
  {
    id: 'consultation',
    label: 'Khám lâm sàng & EMR',
    icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z',
  },
  {
    id: 'diagnostics',
    label: 'Chỉ định & Kết quả CLS',
    badge: '2 mới',
    icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4',
  },
  {
    id: 'prescriptions',
    label: 'Kê đơn thuốc điện tử',
    icon: 'M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z',
  },
];

export function DoctorSidebar({
  activeItem = 'dashboard',
  doctorName = 'BS. CKII Nguyễn Văn Hùng',
  roomLabel = 'P.102 • Khoa Nội Tổng Quát',
  onItemSelect,
}: DoctorSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);

  return (
    <aside
      aria-label="Điều hướng bàn khám bác sĩ"
      className={`bg-white border-r border-slate-200 flex flex-col transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      }`}
    >
      {/* Brand + collapse */}
      <div
        className={`relative h-16 border-b border-slate-200 flex items-center ${
          isCollapsed ? 'justify-center px-2' : 'justify-between px-4'
        }`}
      >
        <div className="flex items-center gap-2.5 overflow-hidden">
          <MedflowLogo variant="full" className="h-9 w-auto flex-shrink-0" linkToHome={false} />
          {!isCollapsed && (
            <div className="text-[9px] font-mono text-slate-400 uppercase truncate">
              Bàn khám • CS1
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={() => setIsCollapsed(!isCollapsed)}
          aria-label={isCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
          aria-expanded={!isCollapsed}
          className={
            isCollapsed
              ? 'absolute -right-3 top-1/2 -translate-y-1/2 z-20 flex h-6 w-6 items-center justify-center rounded-full border border-slate-200 bg-white text-slate-500 shadow-xs hover:bg-slate-100 hover:text-slate-700 transition-colors'
              : 'p-1 rounded-md text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors'
          }
          title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
        >
          <svg
            aria-hidden="true"
            className="w-4 h-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
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
        <div className="m-3 p-3 rounded-xl bg-sky-50 border border-sky-100 flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-sky-100 border border-sky-200 text-sky-700 flex items-center justify-center font-bold text-xs">
            BS
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[9px] font-mono uppercase text-sky-700 font-bold tracking-wider">
              Doctor Workbench
            </div>
            <div className="text-xs font-bold text-slate-800 truncate">{doctorName}</div>
            <div className="text-[10px] text-slate-500 truncate">{roomLabel}</div>
          </div>
          <span className="w-2 h-2 rounded-full bg-emerald-500 ring-2 ring-emerald-200" />
        </div>
      )}

      {/* Nav — chỉ hiển thị các lối vào được phép của vai trò Doctor */}
      <nav className="px-3 py-2 space-y-1 flex-1">
        {!isCollapsed && (
          <div className="px-2 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
            Nghiệp vụ bác sĩ
          </div>
        )}
        {DOCTOR_NAV_ITEMS.map((item) => {
          const isActive = activeItem === item.id;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => onItemSelect?.(item.id)}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-semibold transition-all ${
                isActive
                  ? 'bg-sky-600 text-white shadow-sm shadow-sky-600/30'
                  : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
              }`}
              title={item.label}
            >
              <div className="flex items-center gap-2.5 min-w-0">
                <svg
                  aria-hidden="true"
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
                      : 'bg-sky-50 text-sky-700 border border-sky-200'
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
              <span className="text-sky-600 font-semibold">#P102</span>
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
