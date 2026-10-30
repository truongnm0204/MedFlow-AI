import { ChevronDown, Stethoscope } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { ClinicalSidebar } from './clinical-sidebar';

export type AdminPortalTab =
  | 'Khoa phòng'
  | 'Buồng khám & Kỹ thuật'
  | 'Bác sĩ & Nhân sự Y tế'
  | 'Danh mục Dịch vụ & Kỹ thuật'
  | 'Bảng giá & Biểu phí'
  | 'Quản lý Slot khám'
  | 'Chính sách hẹn';

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

export function AdminPortalShell({
  children,
  showSidebar = true,
  activeTab = 'Danh mục Dịch vụ & Kỹ thuật',
  breadcrumbText,
  breadcrumbCode,
  breadcrumbLink,
  breadcrumbs,
}: AdminPortalShellProps) {
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

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      {showSidebar && <ClinicalSidebar variant="admin" activeItem={sidebarActiveItem} />}

      <div className="flex flex-1 flex-col min-w-0">
        <header className="border-b border-border bg-white">
          <div className="flex h-16 items-center justify-between gap-5 px-6">
            <div className="flex min-w-0 items-center gap-5">
              {!showSidebar ? (
                <Link
                  to={activeTab === 'Bác sĩ & Nhân sự Y tế' ? '/admin/doctors' : '/admin/services'}
                  className="flex shrink-0 items-center gap-2.5"
                  aria-label="MedFlow HIS Hospital Admin Portal"
                >
                  <span className="grid h-9 w-9 place-items-center rounded-md bg-primary text-white shadow-sm">
                    <Stethoscope className="h-5 w-5 text-white" aria-hidden="true" />
                  </span>
                  <span className="leading-tight">
                    <span className="block text-base font-bold tracking-tight">
                      MedFlow <span className="text-primary">HIS</span>{' '}
                      <span className="rounded bg-sky-50 px-1.5 py-0.5 text-[10px] font-bold text-primary">
                        HOSPITAL ADMIN
                      </span>
                    </span>
                    <span className="block text-[10px] text-sub">Hospital Admin Portal</span>
                  </span>
                </Link>
              ) : (
                <div className="flex items-center gap-2">
                  <span className="rounded bg-sky-50 px-2 py-1 text-xs font-bold text-primary font-mono">
                    HOSPITAL ADMIN
                  </span>
                </div>
              )}

              <div className="hidden h-7 w-px bg-border md:block" />

              <div className="hidden items-center gap-2 text-xs text-sub md:flex">
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
                      <Link
                        to={breadcrumbLink}
                        className="font-semibold text-ink hover:text-primary"
                      >
                        {breadcrumbText}
                      </Link>
                    ) : (
                      <span className="font-semibold text-ink">{breadcrumbText}</span>
                    )}
                    {breadcrumbCode && (
                      <span className="rounded bg-slate-100 px-2 py-0.5 font-mono text-[10px] text-sub">
                        {breadcrumbCode}
                      </span>
                    )}
                  </>
                )}
              </div>
            </div>

            <div className="flex shrink-0 items-center gap-4">
              <span className="hidden rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[10px] font-semibold text-emerald-700 lg:inline-flex">
                ● Dữ liệu minh họa · Chưa đồng bộ
              </span>
              <span className="hidden rounded border border-border bg-canvas px-3 py-1.5 font-mono text-[10px] text-sub xl:inline-flex">
                Bản xem trước
              </span>

              <div className="hidden text-right text-[11px] md:block">
                <div className="font-bold text-ink">
                  {user?.fullName ?? user?.email ?? 'DS. Nguyễn Văn Quản'}
                </div>
                <div className="text-sub">
                  <b className="text-primary">
                    {user?.roles.includes('SYSTEM_ADMIN') ? 'SYSTEM_ADMIN' : 'ADM-001'}
                  </b>{' '}
                  • Quản trị Hệ thống
                </div>
              </div>

              <div className="flex items-center gap-1.5">
                <div
                  className="grid h-9 w-9 place-items-center rounded-full bg-navy text-xs font-bold text-white shadow-sm ring-2 ring-slate-100"
                  title={user?.fullName ?? 'DS. Nguyễn Văn Quản'}
                >
                  NVQ
                </div>
                <ChevronDown className="h-4 w-4 text-slate-400" aria-hidden="true" />
              </div>
            </div>
          </div>
        </header>

        <div className="flex-1">{children}</div>

        <footer className="border-t border-border bg-white px-6 py-4 text-[11px] text-sub">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-2">
              <span className="text-primary font-bold">🛡️ 2026 MedFlow HIS</span>
              <span>•</span>
              <span>Giao diện Quản trị Khám ngoại trú</span>
            </div>

            <div className="flex flex-wrap items-center gap-6 text-slate-500">
              <span className="hover:text-primary cursor-pointer">Quy định Bảo mật Dữ liệu</span>
              <span className="hover:text-primary cursor-pointer">Điều khoản Sử dụng</span>
              <span className="hover:text-primary cursor-pointer">Chính sách dữ liệu</span>
              <span className="font-semibold text-slate-700">📞 Hotline 24/7: 1900 6868</span>
            </div>
          </div>
        </footer>
      </div>
    </div>
  );
}
