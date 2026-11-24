import type { LucideIcon } from 'lucide-react';
import {
  CheckCircle2,
  ChevronsLeft,
  ChevronsRight,
  LayoutDashboard,
  PackageCheck,
  Search,
  ShieldCheck,
} from 'lucide-react';
import { useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

interface PharmacySidebarProps {
  activeItem?: string;
  pharmacistName?: string;
  counterLabel?: string;
  onItemSelect?: (item: string) => void;
}

interface PharmacyNavItem {
  id: string;
  label: string;
  badge?: string;
  badgeTone?: 'amber' | 'sky' | 'emerald';
  icon: LucideIcon;
}

const NAV_ITEMS: PharmacyNavItem[] = [
  {
    id: 'dashboard',
    label: 'Tổng quan nhà thuốc',
    icon: LayoutDashboard,
  },
  {
    id: 'search',
    label: 'Tra cứu đơn thuốc',
    icon: Search,
  },
  {
    id: 'prepare',
    label: 'Soạn thuốc & lô FEFO',
    icon: PackageCheck,
  },
  {
    id: 'dispense',
    label: 'Xác nhận cấp phát',
    icon: CheckCircle2,
  },
];

const BADGE_TONE_CLASS: Record<NonNullable<PharmacyNavItem['badgeTone']>, string> = {
  amber: 'border-amber-200 bg-amber-50 text-amber-700',
  sky: 'border-sky-200 bg-sky-50 text-sky-700',
  emerald: 'border-emerald-200 bg-emerald-50 text-emerald-700',
};

export function PharmacySidebar({
  activeItem = 'dashboard',
  pharmacistName,
  counterLabel,
  onItemSelect,
}: PharmacySidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const user = useAuthStore((s) => s.user);
  const resolvedPharmacistName =
    pharmacistName?.trim() || user?.fullName?.trim() || 'Dược sĩ nhà thuốc';
  const resolvedCounterLabel =
    counterLabel?.trim() ||
    (user?.staffCode?.trim() ? `Mã NV: ${user.staffCode.trim()}` : 'Quầy Dược ngoại trú');

  return (
    <aside
      aria-label="Điều hướng nhà thuốc"
      className={`flex flex-col border-r border-border bg-white transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      }`}
    >
      {/* Brand + collapse toggle */}
      <div
        className={`relative flex h-16 items-center border-b border-border ${
          isCollapsed ? 'justify-center px-2' : 'justify-between px-4'
        }`}
      >
        <div className="flex items-center gap-2.5 overflow-hidden">
          <MedflowLogo variant="full" className="h-9 w-auto shrink-0" linkToHome={false} />
          {!isCollapsed && (
            <div className="truncate font-mono text-[9px] text-sub uppercase">Khối Dược</div>
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
            DS
          </div>
          <div className="min-w-0 flex-1">
            <div className="font-mono text-[9px] font-bold tracking-wider text-primary uppercase">
              Pharmacy Workbench
            </div>
            <div className="truncate text-xs font-bold text-ink">{resolvedPharmacistName}</div>
            <div className="truncate text-[10px] text-sub">{resolvedCounterLabel}</div>
          </div>
        </div>
      )}

      {/* Navigation */}
      <nav aria-label="Menu nghiệp vụ nhà thuốc" className="flex-1 space-y-1 px-3 py-2">
        {!isCollapsed && (
          <div className="mb-1 px-2 font-mono text-[10px] font-bold tracking-wider text-sub uppercase">
            Nghiệp vụ nhà thuốc
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
                  className={`shrink-0 rounded-full border px-1.5 py-0.5 font-mono text-[10px] font-bold whitespace-nowrap ${
                    isActive
                      ? 'border-transparent bg-white/20 text-white'
                      : BADGE_TONE_CLASS[item.badgeTone ?? 'sky']
                  }`}
                >
                  {item.badge}
                </span>
              )}
            </button>
          );
        })}

        {!isCollapsed && (
          <div className="mt-4 rounded-xl border border-dashed border-border bg-canvas p-3 text-[10px] text-sub">
            <div className="flex items-center gap-1.5 font-mono text-[9px] font-bold text-ink uppercase">
              <ShieldCheck aria-hidden="true" className="h-3.5 w-3.5 shrink-0 text-primary" />
              <span>Phân quyền Dược sĩ (RBAC)</span>
            </div>
            <p className="mt-1 leading-relaxed">
              Chỉ hiển thị UC44, UC45, UC46. Ẩn tác vụ khám lâm sàng, chỉ định CLS và cấu hình
              Admin.
            </p>
          </div>
        )}
      </nav>

      {/* Footer info */}
      <div className="border-t border-border bg-canvas p-3">
        {!isCollapsed ? (
          <div className="space-y-1 rounded-lg border border-border bg-white p-2 text-[10px] shadow-2xs">
            <div className="flex items-center justify-between font-semibold text-ink">
              <span>Nhà thuốc ngoại trú</span>
            </div>
            <div className="flex items-center justify-between font-mono text-sub">
              <span>Quy tắc FEFO</span>
              <span className="font-semibold text-primary">1900 6868</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center font-mono text-[9px] font-bold text-sub">DS</div>
        )}
      </div>
    </aside>
  );
}
