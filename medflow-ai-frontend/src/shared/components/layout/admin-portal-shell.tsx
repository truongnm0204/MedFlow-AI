import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { useDoctorDemoStore } from '@/features/admin-doctors/store/doctor-demo.store';
import { useExaminationServiceStore } from '@/features/admin-services/store/examination-service.store';
import { useAuthStore } from '@/features/auth/store/auth.store';

export type AdminPortalTab =
  | 'Khoa phòng'
  | 'Buồng khám & Kỹ thuật'
  | 'Bác sĩ & Nhân sự Y tế'
  | 'Danh mục Dịch vụ & Kỹ thuật'
  | 'Bảng giá & Biểu phí'
  | 'Quản lý Slot khám'
  | 'Chính sách hẹn';

export interface AdminPortalShellProps {
  children: ReactNode;
  showTabs?: boolean;
  activeTab?: AdminPortalTab;
  breadcrumbText?: string;
  breadcrumbCode?: string;
  breadcrumbLink?: string;
}

const TABS: { name: AdminPortalTab; href?: string }[] = [
  { name: 'Khoa phòng' },
  { name: 'Buồng khám & Kỹ thuật' },
  { name: 'Bác sĩ & Nhân sự Y tế', href: '/admin/doctors' },
  { name: 'Danh mục Dịch vụ & Kỹ thuật', href: '/admin/services' },
  { name: 'Bảng giá & Biểu phí' },
  { name: 'Quản lý Slot khám' },
  { name: 'Chính sách hẹn' },
];

export function AdminPortalShell({
  children,
  showTabs = true,
  activeTab = 'Danh mục Dịch vụ & Kỹ thuật',
  breadcrumbText,
  breadcrumbCode,
  breadcrumbLink,
}: AdminPortalShellProps) {
  const user = useAuthStore((state) => state.user);
  const doctorCount = useDoctorDemoStore((state) => state.doctors.length);
  const serviceCount = useExaminationServiceStore((state) => state.services.length);

  return (
    <div className="min-h-screen bg-canvas font-sans text-ink">
      <header className="border-b border-border bg-white">
        <div className="mx-auto flex h-16 max-w-[1720px] items-center justify-between gap-5 px-6">
          <div className="flex min-w-0 items-center gap-5">
            <Link
              to={activeTab === 'Bác sĩ & Nhân sự Y tế' ? '/admin/doctors' : '/admin/services'}
              className="flex shrink-0 items-center gap-2.5"
              aria-label="MedFlow HIS Hospital Admin Portal"
            >
              <span className="grid h-9 w-9 place-items-center rounded-md bg-primary text-white shadow-sm">
                <img src="/assets/admin-doctors/list-27.svg" alt="" className="h-5 w-5" />
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

            <div className="hidden h-7 w-px bg-border md:block" />

            <div className="hidden items-center gap-2 text-xs text-sub md:flex">
              <span className="text-slate-500">Cấu hình Cơ sở</span>
              <span className="text-slate-400">›</span>
              {breadcrumbLink ? (
                <Link to={breadcrumbLink} className="font-semibold text-ink hover:text-primary">
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
              <svg
                className="h-4 w-4 text-slate-400"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M19 9l-7 7-7-7"
                />
              </svg>
            </div>
          </div>
        </div>

        {showTabs && (
          <nav
            className="overflow-x-auto border-t border-slate-100"
            aria-label="Danh mục quản trị cơ sở"
          >
            <div className="mx-auto flex min-w-max max-w-[1720px] gap-1 px-6">
              {TABS.map((tab) => {
                const isActive = tab.name === activeTab;
                const badgeValue =
                  tab.name === 'Bác sĩ & Nhân sự Y tế'
                    ? doctorCount
                    : tab.name === 'Danh mục Dịch vụ & Kỹ thuật'
                      ? serviceCount
                      : undefined;

                const content = (
                  <span
                    className={`flex items-center border-b-2 px-4 py-3 text-xs transition-colors ${
                      isActive
                        ? 'border-primary font-bold text-primary'
                        : 'border-transparent font-medium text-sub hover:text-ink'
                    }`}
                  >
                    {tab.name}
                    {badgeValue !== undefined && (
                      <span
                        className={`ml-2 rounded px-1.5 py-0.5 text-[10px] font-semibold ${
                          isActive ? 'bg-sky-50 text-primary' : 'bg-slate-100 text-sub'
                        }`}
                      >
                        {badgeValue}
                      </span>
                    )}
                  </span>
                );

                if (tab.href) {
                  return (
                    <Link key={tab.name} to={tab.href}>
                      {content}
                    </Link>
                  );
                }
                return (
                  <button key={tab.name} type="button">
                    {content}
                  </button>
                );
              })}
            </div>
          </nav>
        )}
      </header>

      {children}

      <footer className="border-t border-border bg-white px-6 py-4 text-[11px] text-sub">
        <div className="mx-auto flex max-w-[1720px] flex-wrap items-center justify-between gap-4">
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
  );
}
