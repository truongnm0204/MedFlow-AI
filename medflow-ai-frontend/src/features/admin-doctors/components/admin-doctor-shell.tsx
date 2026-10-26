import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { useDoctorDemoStore } from '../store/doctor-demo.store';

interface AdminDoctorShellProps {
  children: ReactNode;
  showTabs?: boolean;
}

const TABS = [
  'Khoa phòng',
  'Buồng khám & Kỹ thuật',
  'Bác sĩ & Nhân sự Y tế',
  'Danh mục Dịch vụ & Kỹ thuật',
  'Bảng giá & Biểu phí',
  'Quản lý Slot khám',
  'Chính sách hẹn',
];

/** Shared hospital administration chrome from the doctor Figma frames. */
export function AdminDoctorShell({ children, showTabs = false }: AdminDoctorShellProps) {
  const user = useAuthStore((state) => state.user);
  const doctorCount = useDoctorDemoStore((state) => state.doctors.length);
  return (
    <div className="min-h-screen bg-canvas text-ink">
      <header className="border-b border-border bg-white">
        <div className="mx-auto flex h-16 max-w-[1720px] items-center justify-between gap-5 px-6">
          <div className="flex min-w-0 items-center gap-5">
            <Link
              to="/admin/doctors"
              className="flex shrink-0 items-center gap-2.5"
              aria-label="Danh mục Bác sĩ"
            >
              <span className="grid h-9 w-9 place-items-center rounded-md bg-primary">
                <img src="/assets/admin-doctors/list-27.svg" alt="" />
              </span>
              <span className="leading-tight">
                <span className="block text-base font-bold tracking-tight">
                  MedFlow <span className="text-primary">HIS</span>{' '}
                  <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-sub">
                    ADM
                  </span>
                </span>
                <span className="block text-[10px] text-sub">Hospital Admin Portal</span>
              </span>
            </Link>
            <div className="hidden h-7 w-px bg-border md:block" />
            <div className="hidden items-center gap-2 text-xs text-sub md:flex">
              <span>Cấu hình Cơ sở</span>
              <span>›</span>
              <NavLink to="/admin/doctors" className="font-semibold text-ink">
                Danh mục Bác sĩ
              </NavLink>
              <span className="rounded bg-slate-100 px-2 py-1 font-mono text-[10px] text-sub">
                SCR-ADM-DOC
              </span>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-5">
            <span className="hidden rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[10px] font-semibold text-emerald-700 lg:inline-flex">
              ● Dữ liệu minh họa · Chưa đồng bộ
            </span>
            <span className="hidden rounded border border-border bg-canvas px-3 py-1.5 font-mono text-[10px] text-sub xl:inline-flex">
              Bản xem trước
            </span>
            <div className="hidden text-right text-[11px] md:block">
              <div className="font-bold text-ink">
                {user?.fullName ?? user?.email ?? 'Quản trị viên'}
              </div>
              <div className="text-sub">
                <b className="text-primary">
                  {user?.roles.includes('SYSTEM_ADMIN') ? 'SYSTEM_ADMIN' : 'HOSPITAL_ADMIN'}
                </b>{' '}
                • Quản trị Hệ thống
              </div>
            </div>
            <img
              src="/assets/admin-doctors/list-01.png"
              alt=""
              className="h-9 w-9 rounded-full border border-border object-cover"
            />
          </div>
        </div>
        {showTabs && (
          <nav className="overflow-x-auto border-t border-slate-100" aria-label="Danh mục quản trị">
            <div className="mx-auto flex min-w-max max-w-[1720px] gap-1 px-6">
              {TABS.map((tab) => (
                <span
                  key={tab}
                  className={`border-b-2 px-4 py-3 text-xs ${tab === 'Bác sĩ & Nhân sự Y tế' ? 'border-primary font-bold text-primary' : 'border-transparent text-sub'}`}
                >
                  {tab}
                  {tab === 'Bác sĩ & Nhân sự Y tế' && (
                    <span className="ml-2 rounded bg-sky-50 px-1.5 py-0.5 text-[10px]">
                      {doctorCount}
                    </span>
                  )}
                </span>
              ))}
            </div>
          </nav>
        )}
      </header>
      {children}
      {showTabs && (
        <footer className="border-t border-border bg-canvas px-6 py-5 text-[10px] text-sub">
          <div className="mx-auto flex max-w-[1720px] flex-wrap items-center justify-between gap-4">
            <div>
              <b>© 2025 MedFlowAI</b> - Nền tảng Quản trị Y tế Thông minh Chuẩn HL7/FHIR.{' '}
              <span className="block text-slate-400">
                Tiêu chuẩn FHIR Release 4.0.1 • Hồ sơ trên màn hình này là dữ liệu minh họa.
              </span>
            </div>
            <div className="flex gap-5">
              <span>Quy định Bảo mật Dữ liệu</span>
              <span>Điều khoản Sử dụng</span>
              <span>Tiêu chuẩn HL7/FHIR</span>
              <span>Hỗ trợ 24/7</span>
            </div>
          </div>
        </footer>
      )}
    </div>
  );
}
