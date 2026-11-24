import type { LucideIcon } from 'lucide-react';
import {
  Calendar,
  ChevronsLeft,
  ChevronsRight,
  ClipboardCheck,
  Layers,
  LayoutDashboard,
  ShieldCheck,
  Users,
} from 'lucide-react';
import { useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

interface ReceptionSidebarProps {
  activeItem?: string;
  onItemSelect?: (item: string) => void;
}

interface ReceptionNavItem {
  id: string;
  label: string;
  badge?: string;
  icon: LucideIcon;
}

const NAV_ITEMS: ReceptionNavItem[] = [
  {
    id: 'dashboard',
    label: 'Tổng quan quầy tiếp đón',
    icon: LayoutDashboard,
  },
  {
    id: 'check-in',
    label: 'Check-in & Tiếp nhận',
    icon: ClipboardCheck,
  },
  {
    id: 'queue',
    label: 'Hàng đợi khám',
    icon: Layers,
  },
  {
    id: 'patients',
    label: 'Hồ sơ bệnh nhân',
    icon: Users,
  },
  {
    id: 'identity-claims',
    label: 'Xác minh hồ sơ',
    icon: ShieldCheck,
  },
  {
    id: 'appointments',
    label: 'Lịch hẹn hôm nay',
    icon: Calendar,
  },
];

export function ReceptionSidebar({
  activeItem = 'dashboard',
  onItemSelect,
}: ReceptionSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const user = useAuthStore((s) => s.user);
  const staffDisplayName = user?.fullName?.trim() || 'Nhân viên tiếp đón';
  const subLabel = user?.staffCode?.trim()
    ? `Mã NV: ${user.staffCode.trim()}`
    : 'Quầy tiếp đón ngoại trú';

  return (
    <aside
      aria-label="Điều hướng quầy tiếp đón"
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
            <div className="truncate font-mono text-[9px] text-sub uppercase">Quầy tiếp đón</div>
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
        <div className="m-3 flex items-center gap-2.5 rounded-xl border border-emerald-100 bg-emerald-50 p-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-emerald-200 bg-emerald-100 text-xs font-bold text-emerald-700">
            LT
          </div>
          <div className="min-w-0 flex-1">
            <div className="font-mono text-[9px] font-bold tracking-wider text-emerald-700 uppercase">
              Reception Desk
            </div>
            <div className="truncate text-xs font-bold text-ink">{staffDisplayName}</div>
            <div className="truncate text-[10px] text-sub">{subLabel}</div>
          </div>
        </div>
      )}

      {/* Nav */}
      <nav aria-label="Menu nghiệp vụ tiếp đón" className="flex-1 space-y-1 px-3 py-2">
        {!isCollapsed && (
          <div className="mb-1 px-2 font-mono text-[10px] font-bold tracking-wider text-sub uppercase">
            Nghiệp vụ tiếp đón
          </div>
        )}
        {NAV_ITEMS.map((item) => {
          const isActive = activeItem === item.id;
          const Icon = item.icon;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => onItemSelect?.(item.id)}
              aria-current={isActive ? 'page' : undefined}
              className={`flex w-full items-center justify-between rounded-xl px-3 py-2 text-xs font-semibold transition-all focus-visible:ring-2 focus-visible:ring-emerald-600 focus-visible:outline-none ${
                isActive
                  ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/30'
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
                      : 'border border-emerald-200 bg-emerald-50 text-emerald-700'
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
              <span>Quầy tiếp đón ngoại trú</span>
            </div>
            <div className="flex items-center justify-between font-mono text-sub">
              <span>100% Non-BHYT</span>
              <span className="font-semibold text-emerald-700">1900 6868</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center font-mono text-[9px] font-bold text-sub">LT</div>
        )}
      </div>
    </aside>
  );
}
