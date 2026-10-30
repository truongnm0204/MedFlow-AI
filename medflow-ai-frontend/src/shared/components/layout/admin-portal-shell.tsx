import {
  BarChart3,
  Building2,
  CalendarClock,
  ChevronDown,
  ChevronsLeft,
  ChevronsRight,
  ClipboardList,
  CreditCard,
  DoorOpen,
  Phone,
  ShieldCheck,
  SlidersHorizontal,
  UserCheck,
  type LucideIcon,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { MedflowLogo } from './medflow-logo';

export type AdminPortalTab =
  | 'Khoa phòng'
  | 'Buồng khám & Kỹ thuật'
  | 'Bác sĩ & Nhân sự Y tế'
  | 'Danh mục Dịch vụ & Kỹ thuật'
  | 'Bảng giá & Biểu phí'
  | 'Quản lý Slot khám'
  | 'Chính sách hẹn'
  | 'Báo cáo vận hành';

export interface AdminPortalBreadcrumb {
  label: string;
  href?: string;
}

export interface AdminPortalShellProps {
  children: ReactNode;
  showTabs?: boolean;
  showSidebar?: boolean;
  activeTab?: AdminPortalTab;
  breadcrumbText?: string;
  breadcrumbCode?: string;
  breadcrumbLink?: string;
  breadcrumbs?: AdminPortalBreadcrumb[];
}

const TABS: { name: AdminPortalTab; href?: string; icon: LucideIcon }[] = [
  { name: 'Khoa phòng', icon: Building2 },
  { name: 'Buồng khám & Kỹ thuật', icon: DoorOpen },
  { name: 'Bác sĩ & Nhân sự Y tế', href: '/admin/doctors', icon: UserCheck },
  { name: 'Danh mục Dịch vụ & Kỹ thuật', href: '/admin/services', icon: ClipboardList },
  { name: 'Bảng giá & Biểu phí', icon: CreditCard },
  { name: 'Quản lý Slot khám', icon: CalendarClock },
  { name: 'Chính sách hẹn', icon: SlidersHorizontal },
  { name: 'Báo cáo vận hành', href: '/admin/reports', icon: BarChart3 },
];

export function AdminPortalShell({
  children,
  activeTab = 'Danh mục Dịch vụ & Kỹ thuật',
  breadcrumbText,
  breadcrumbLink,
  breadcrumbs,
}: AdminPortalShellProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const user = useAuthStore((state) => state.user);

  const sidebarActiveItem =
    activeTab === 'Bác sĩ & Nhân sự Y tế'
      ? 'doctors'
      : activeTab === 'Danh mục Dịch vụ & Kỹ thuật'
        ? 'services'
        : activeTab === 'Khoa phòng'
          ? 'departments'
          : activeTab === 'Buồng khám & Kỹ thuật'
            ? 'rooms'
            : activeTab === 'Bảng giá & Biểu phí'
              ? 'pricing'
              : activeTab === 'Quản lý Slot khám'
                ? 'slots'
                : activeTab === 'Chính sách hẹn'
                  ? 'policies'
                  : 'services';

  const displayName = user?.fullName ?? user?.email ?? 'DS. Nguyễn Văn Quản';
  const roleLabel = user?.roles.includes('SYSTEM_ADMIN') ? 'SYSTEM_ADMIN' : 'ADM-001';

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      {/* Left Collapsible Sidebar */}
      <aside
        aria-label="Danh mục quản trị cơ sở"
        className={`flex shrink-0 select-none flex-col justify-between border-r border-border bg-white transition-all duration-200 ${
          isCollapsed ? 'w-[68px]' : 'w-[260px]'
        }`}
      >
        <div>
          {/* Sidebar Brand + Collapse Toggle */}
          <div className="flex h-16 items-center justify-between border-b border-border px-4">
            <div className="flex items-center gap-2.5 overflow-hidden">
              <MedflowLogo
                variant={isCollapsed ? 'icon' : 'full'}
                className={isCollapsed ? 'h-8 w-auto shrink-0' : 'h-9 w-auto shrink-0'}
              />
            </div>
            <button
              type="button"
              onClick={() => setIsCollapsed((prev) => !prev)}
              aria-label={isCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
              aria-expanded={!isCollapsed}
              className="rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
              title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
            >
              {isCollapsed ? (
                <ChevronsRight className="h-4 w-4" aria-hidden="true" />
              ) : (
                <ChevronsLeft className="h-4 w-4" aria-hidden="true" />
              )}
            </button>
          </div>

          {/* Role Identity Card */}
          {!isCollapsed && (
            <div className="m-3 flex items-center gap-2.5 rounded-xl border border-sky-100 bg-sky-50 p-3">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-sky-200 bg-sky-100 text-xs font-bold text-primary">
                AD
              </div>
              <div className="min-w-0 flex-1">
                <div className="font-mono text-[9px] font-bold uppercase tracking-wider text-primary">
                  HOSPITAL ADMIN
                </div>
                <div className="truncate text-xs font-bold text-ink">{displayName}</div>
                <div className="truncate text-[10px] text-sub">Cơ sở Hà Nội • {roleLabel}</div>
              </div>
              <span className="h-2 w-2 shrink-0 rounded-full bg-emerald-500 ring-2 ring-emerald-200" />
            </div>
          )}

          {/* Sidebar Navigation Items */}
          <nav className="space-y-1 px-3 py-2" aria-label="Điều hướng cấu hình cơ sở">
            {!isCollapsed && (
              <div className="mb-1 px-2 font-mono text-[10px] font-bold uppercase tracking-wider text-slate-400">
                Quản trị cơ sở
              </div>
            )}

            {TABS.map((tab) => {
              const isActive = tab.name === activeTab;
              const IconComponent = tab.icon;
              const badgeValue =
                tab.name === 'Bác sĩ & Nhân sự Y tế'
                  ? doctorCount
                  : tab.name === 'Danh mục Dịch vụ & Kỹ thuật'
                    ? serviceCount
                    : undefined;

              const itemClassName = `flex w-full items-center justify-between rounded-xl px-3 py-2.5 text-xs font-semibold transition-all ${
                isActive
                  ? 'bg-primary text-white shadow-sm'
                  : 'text-sub hover:bg-slate-50 hover:text-ink'
              }`;

              const itemInner = (
                <>
                  <div className="flex min-w-0 items-center gap-2.5">
                    <IconComponent
                      className={`h-4 w-4 shrink-0 ${isActive ? 'text-white' : 'text-slate-400'}`}
                      aria-hidden="true"
                    />
                    {!isCollapsed && <span className="truncate">{tab.name}</span>}
                  </div>
                  {!isCollapsed && badgeValue !== undefined && (
                    <span
                      className={`rounded-full px-1.5 py-0.5 font-mono text-[10px] font-bold ${
                        isActive
                          ? 'bg-white/20 text-white'
                          : 'border border-sky-200 bg-sky-50 text-primary'
                      }`}
                    >
                      {badgeValue}
                    </span>
                  )}
                </>
              );

              if (tab.href) {
                return (
                  <Link
                    key={tab.name}
                    to={tab.href}
                    className={itemClassName}
                    title={tab.name}
                    aria-current={isActive ? 'page' : undefined}
                  >
                    {itemInner}
                  </Link>
                );
              }

              return (
                <button key={tab.name} type="button" className={itemClassName} title={tab.name}>
                  {itemInner}
                </button>
              );
            })}
          </nav>
        </div>

        {/* Sidebar Footer Status */}
        <div className="border-t border-border bg-slate-50 p-3">
          {!isCollapsed ? (
            <div className="space-y-1 rounded-lg border border-border bg-white p-2 text-[10px] shadow-2xs">
              <div className="flex items-center justify-between font-semibold text-slate-700">
                <span>HIS ADMIN: ONLINE</span>
                <span className="h-2 w-2 rounded-full bg-emerald-500" />
              </div>
              <div className="flex items-center justify-between font-mono text-slate-400">
                <span>Cơ sở Hà Nội</span>
                <span className="font-semibold text-primary">#{roleLabel}</span>
              </div>
            </div>
          ) : (
            <div className="flex justify-center" title="HIS Admin Online">
              <span className="h-2.5 w-2.5 rounded-full bg-emerald-500 ring-2 ring-emerald-200" />
            </div>
          )}
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Clean Top Header (64px) */}
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-2 text-xs text-sub">
            {breadcrumbs && breadcrumbs.length > 0 ? (
              breadcrumbs.map((crumb, idx) => (
                <div key={crumb.label} className="flex items-center gap-2">
                  {idx > 0 && <span className="text-slate-400">›</span>}
                  {crumb.href ? (
                    <Link to={crumb.href} className="text-slate-500 hover:text-primary">
                      {crumb.label}
                    </Link>
                  ) : (
                    <span className="font-semibold text-ink">{crumb.label}</span>
                  )}
                </div>
              ))
            ) : (
              <>
                <span className="text-slate-500">Cấu hình Cơ sở</span>
                <span className="text-slate-400">›</span>
                {breadcrumbLink ? (
                  <Link to={breadcrumbLink} className="font-semibold text-ink hover:text-primary">
                    {breadcrumbText}
                  </Link>
                ) : (
                  <span className="font-semibold text-ink">{breadcrumbText}</span>
                )}
              </>
            )}
          </div>

          <div className="flex shrink-0 items-center gap-4">
            <div className="hidden text-right text-[11px] md:block">
              <div className="font-bold text-ink">{displayName}</div>
              <div className="text-sub">
                <b className="text-primary">{roleLabel}</b> • Quản trị Hệ thống
              </div>
            </div>

            <div className="flex items-center gap-1.5">
              <div
                className="grid h-9 w-9 place-items-center rounded-full bg-navy text-xs font-bold text-white shadow-sm ring-2 ring-slate-100"
                title={displayName}
              >
                NVQ
              </div>
              <ChevronDown className="h-4 w-4 text-slate-400" aria-hidden="true" />
            </div>
          </div>
        </header>

        <div className="flex-1">{children}</div>

        <footer className="border-t border-border bg-white px-6 py-4 text-[11px] text-sub">
          <div className="mx-auto flex max-w-[1720px] flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              <span className="font-bold text-primary">2026 MedFlow HIS</span>
              <span>•</span>
              <span>Giao diện Quản trị Khám ngoại trú</span>
            </div>

            <div className="flex flex-wrap items-center gap-6 text-slate-500">
              <span className="cursor-pointer hover:text-primary">Quy định Bảo mật Dữ liệu</span>
              <span className="cursor-pointer hover:text-primary">Điều khoản Sử dụng</span>
              <span className="cursor-pointer hover:text-primary">Chính sách dữ liệu</span>
              <span className="inline-flex items-center gap-1.5 font-semibold text-slate-700">
                <Phone className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                Hotline 24/7: 1900 6868
              </span>
            </div>
          </div>
        </footer>
      </div>
    </div>
  );
}
