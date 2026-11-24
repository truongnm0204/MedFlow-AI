import type { LucideIcon } from 'lucide-react';
import {
  ChevronsLeft,
  ChevronsRight,
  FileText,
  Layers,
  LayoutDashboard,
  Microscope,
  Pill,
} from 'lucide-react';
import { useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

interface DoctorSidebarProps {
  activeItem?: string;
  doctorName?: string;
  roomLabel?: string;
  onItemSelect?: (item: string) => void;
}

interface DoctorNavItem {
  id: string;
  label: string;
  badge?: string;
  icon: LucideIcon;
}

const DOCTOR_NAV_ITEMS: DoctorNavItem[] = [
  {
    id: 'dashboard',
    label: 'Tổng quan bàn khám',
    icon: LayoutDashboard,
  },
  {
    id: 'assigned-queue',
    label: 'Hàng đợi phân công',
    icon: Layers,
  },
  {
    id: 'consultation',
    label: 'Khám lâm sàng & EMR',
    icon: FileText,
  },
  {
    id: 'diagnostics',
    label: 'Chỉ định & Kết quả CLS',
    icon: Microscope,
  },
  {
    id: 'prescriptions',
    label: 'Kê đơn thuốc điện tử',
    icon: Pill,
  },
];

export function DoctorSidebar({
  activeItem = 'dashboard',
  doctorName,
  roomLabel,
  onItemSelect,
}: DoctorSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const user = useAuthStore((s) => s.user);
  const resolvedDoctorName = doctorName?.trim() || user?.fullName?.trim() || 'Bác sĩ lâm sàng';
  const resolvedRoomLabel =
    roomLabel?.trim() ||
    (user?.staffCode?.trim() ? `Mã NV: ${user.staffCode.trim()}` : 'Bàn khám ngoại trú');

  return (
    <aside
      aria-label="Điều hướng bàn khám bác sĩ"
      className={`flex flex-col border-r border-border bg-white transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      }`}
    >
      {/* Brand + collapse */}
      <div
        className={`relative flex h-16 items-center border-b border-border ${
          isCollapsed ? 'justify-center px-2' : 'justify-between px-4'
        }`}
      >
        <div className="flex items-center gap-2.5 overflow-hidden">
          <MedflowLogo variant="full" className="h-9 w-auto shrink-0" linkToHome={false} />
          {!isCollapsed && (
            <div className="truncate font-mono text-[9px] text-sub uppercase">Bàn khám</div>
          )}
        </div>
        <button
          type="button"
          onClick={() => setIsCollapsed(!isCollapsed)}
          aria-label={isCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
          aria-expanded={!isCollapsed}
          className={
            isCollapsed
              ? 'absolute top-1/2 -right-3 z-20 flex h-6 w-6 -translate-y-1/2 items-center justify-center rounded-full border border-border bg-white text-sub shadow-xs transition-colors hover:bg-slate-100 hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none'
              : 'rounded-md p-1 text-sub transition-colors hover:bg-slate-100 hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none'
          }
          title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
        >
          {isCollapsed ? (
            <ChevronsRight aria-hidden="true" className="h-4 w-4" />
          ) : (
            <ChevronsLeft aria-hidden="true" className="h-4 w-4" />
          )}
        </button>
      </div>

      {/* Role identity */}
      {!isCollapsed && (
        <div className="m-3 flex items-center gap-2.5 rounded-xl border border-sky-100 bg-sky-50 p-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-sky-200 bg-sky-100 text-xs font-bold text-primary">
            BS
          </div>
          <div className="min-w-0 flex-1">
            <div className="font-mono text-[9px] font-bold tracking-wider text-primary uppercase">
              Doctor Workbench
            </div>
            <div className="truncate text-xs font-bold text-ink">{resolvedDoctorName}</div>
            <div className="truncate text-[10px] text-sub">{resolvedRoomLabel}</div>
          </div>
        </div>
      )}

      {/* Nav — chỉ hiển thị các lối vào được phép của vai trò Doctor */}
      <nav aria-label="Menu nghiệp vụ bác sĩ" className="flex-1 space-y-1 px-3 py-2">
        {!isCollapsed && (
          <div className="mb-1 px-2 font-mono text-[10px] font-bold tracking-wider text-sub uppercase">
            Nghiệp vụ bác sĩ
          </div>
        )}
        {DOCTOR_NAV_ITEMS.map((item) => {
          const isActive = activeItem === item.id;
          const Icon = item.icon;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => onItemSelect?.(item.id)}
              aria-current={isActive ? 'page' : undefined}
              className={`flex w-full items-center justify-between rounded-xl px-3 py-2 text-xs font-semibold transition-all focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none ${
                isActive
                  ? 'bg-primary text-white shadow-sm shadow-sky-600/30'
                  : 'text-sub hover:bg-slate-50 hover:text-ink'
              }`}
              title={item.label}
            >
              <div className="flex min-w-0 items-center gap-2.5">
                <Icon
                  aria-hidden="true"
                  className={`h-4 w-4 shrink-0 ${isActive ? 'text-white' : 'text-sub'}`}
                />
                {!isCollapsed && <span className="truncate">{item.label}</span>}
              </div>
              {!isCollapsed && item.badge && (
                <span
                  className={`rounded-full px-1.5 py-0.5 font-mono text-[10px] font-bold ${
                    isActive
                      ? 'bg-white/20 text-white'
                      : 'border border-sky-200 bg-sky-50 text-primary'
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
      <div className="border-t border-border bg-canvas p-3">
        {!isCollapsed ? (
          <div className="space-y-1 rounded-lg border border-border bg-white p-2 text-[10px] shadow-2xs">
            <div className="flex items-center justify-between font-semibold text-ink">
              <span>Bàn khám ngoại trú</span>
            </div>
            <div className="flex items-center justify-between font-mono text-sub">
              <span>100% Non-BHYT</span>
              <span className="font-semibold text-primary">1900 6868</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center font-mono text-[9px] font-bold text-sub">BS</div>
        )}
      </div>
    </aside>
  );
}
