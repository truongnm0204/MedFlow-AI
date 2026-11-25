import {
  CheckCircle2,
  ChevronRight,
  FileText,
  FolderHeart,
  Home,
  KeyRound,
  LayoutDashboard,
  Link2,
  LogOut,
  Pill,
  RefreshCw,
  ShieldCheck,
  UserRound,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import { useLogout, useMe } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import {
  getRoleDisplayLabel,
  getRoleLandingRoute,
  isStaffRole,
} from '@/features/auth/utils/role-landing';
import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { ChangePasswordCard } from '../components/change-password-card';
import { PatientRecordsSubnav } from '../components/patient-records-subnav';

export function AccountSettingsPage() {
  const user = useAuthStore((s) => s.user);
  const sessionEpoch = useAuthStore((s) => s.sessionEpoch);
  const meQuery = useMe();
  const logoutMutation = useLogout();

  const roles = user?.roles ?? [];
  const hasPatientRole = roles.includes('PATIENT');
  const isStaff = isStaffRole(roles);
  const roleLabel = getRoleDisplayLabel(roles);
  const workbenchRoute = getRoleLandingRoute(roles);

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-ink">Cài đặt tài khoản</span>
          </nav>

          {hasPatientRole && <PatientRecordsSubnav />}

          <header className="mb-6">
            <h1 className="text-2xl font-bold text-navy">Cài đặt tài khoản & Bảo mật phiên</h1>
            <p className="mt-1 text-xs leading-relaxed text-sub">
              Quản lý thông tin xác thực tài khoản, bảo mật phiên đăng nhập đa tab và lối tắt điều
              hướng hồ sơ sức khỏe trên MedFlow AI.
            </p>
          </header>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <section className="space-y-6 lg:col-span-7">
              <div className="rounded-2xl border border-border bg-white p-6 shadow-xs">
                <div className="flex items-center justify-between gap-3 border-b border-border pb-4">
                  <div className="flex items-center gap-2.5">
                    <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
                    <h2 className="text-base font-bold text-navy">Thông tin định danh & Bảo mật</h2>
                  </div>
                  <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-1 text-[11px] font-semibold text-emerald-700">
                    <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Đã xác thực email OTP</span>
                  </span>
                </div>

                <dl className="mt-4 grid grid-cols-1 gap-4 text-xs sm:grid-cols-2">
                  <div className="rounded-xl bg-canvas p-3.5">
                    <dt className="font-medium text-sub">Họ và tên hiển thị</dt>
                    <dd className="mt-1 text-sm font-bold text-ink">
                      {user?.fullName ?? 'Chưa cập nhật'}
                    </dd>
                  </div>
                  <div className="rounded-xl bg-canvas p-3.5">
                    <dt className="font-medium text-sub">Email đăng nhập</dt>
                    <dd className="mt-1 text-sm font-bold text-ink">{user?.email ?? '—'}</dd>
                  </div>
                  <div className="rounded-xl bg-canvas p-3.5">
                    <dt className="font-medium text-sub">Vai trò hệ thống</dt>
                    <dd className="mt-1 font-semibold text-navy">{roleLabel}</dd>
                  </div>
                  <div className="rounded-xl bg-canvas p-3.5">
                    <dt className="font-medium text-sub">Mã định danh hồ sơ</dt>
                    <dd className="mt-1 font-mono font-bold text-primary">
                      {user?.patientCode ?? user?.staffCode ?? `UID-${user?.id ?? ''}`}
                    </dd>
                  </div>
                </dl>
              </div>

              <ChangePasswordCard />

              <div className="rounded-2xl border border-border bg-white p-6 shadow-xs">
                <div className="flex items-center gap-2.5 border-b border-border pb-4">
                  <KeyRound className="h-5 w-5 text-primary" aria-hidden="true" />
                  <h2 className="text-base font-bold text-navy">Phiên đăng nhập hiện tại</h2>
                </div>

                <div className="mt-4 space-y-3 text-xs text-sub">
                  <p className="leading-relaxed">
                    Phiên làm việc sử dụng cơ chế xoay vòng khóa truy cập tự động và đồng bộ trạng
                    thái đăng xuất đa tab để tự động xóa bộ nhớ đệm dữ liệu y tế nhạy cảm (PHI) khi
                    kết thúc phiên.
                  </p>
                  <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-canvas px-4 py-3">
                    <span>Trạng thái phiên thiết bị hiện tại</span>
                    <span className="font-mono font-semibold text-emerald-700">
                      ĐANG HOẠT ĐỘNG • EPOCH #{sessionEpoch}
                    </span>
                  </div>
                </div>

                <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
                  <button
                    type="button"
                    disabled={meQuery.isFetching}
                    onClick={() => void meQuery.refetch()}
                    className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
                  >
                    <RefreshCw
                      className={`h-3.5 w-3.5 ${meQuery.isFetching ? 'animate-spin' : ''}`}
                      aria-hidden="true"
                    />
                    <span>Đồng bộ lại thông tin phiên</span>
                  </button>

                  <button
                    type="button"
                    disabled={logoutMutation.isPending}
                    onClick={() => logoutMutation.mutate()}
                    className="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-2 text-xs font-semibold text-danger transition-colors hover:bg-rose-100 focus-visible:ring-2 focus-visible:ring-danger disabled:opacity-60"
                  >
                    <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>
                      {logoutMutation.isPending ? 'Đang đăng xuất...' : 'Đăng xuất phiên hiện tại'}
                    </span>
                  </button>
                </div>
              </div>
            </section>

            <aside className="space-y-4 lg:col-span-5">
              <div className="rounded-2xl border border-border bg-white p-6 shadow-xs">
                <h2 className="text-base font-bold text-navy">Điều hướng hồ sơ & Quyền riêng tư</h2>
                <p className="mt-1 text-xs text-sub">
                  Truy cập nhanh các phân hệ quản lý thông tin cá nhân và hồ sơ sức khỏe được phân
                  quyền.
                </p>

                <ul className="mt-4 space-y-2.5">
                  <li>
                    <Link
                      to="/profile"
                      className="flex items-center justify-between rounded-xl border border-border bg-canvas px-4 py-3 text-xs font-semibold text-navy transition-colors hover:border-sky-200 hover:bg-sky-50/50"
                    >
                      <span className="flex items-center gap-2.5">
                        <UserRound className="h-4 w-4 text-primary" aria-hidden="true" />
                        <span>Thông tin hồ sơ cá nhân</span>
                      </span>
                      <ChevronRight className="h-4 w-4 text-sub" aria-hidden="true" />
                    </Link>
                  </li>

                  {hasPatientRole && (
                    <>
                      <li>
                        <Link
                          to="/family"
                          className="flex items-center justify-between rounded-xl border border-border bg-canvas px-4 py-3 text-xs font-semibold text-navy transition-colors hover:border-sky-200 hover:bg-sky-50/50"
                        >
                          <span className="flex items-center gap-2.5">
                            <FolderHeart className="h-4 w-4 text-primary" aria-hidden="true" />
                            <span>Hồ sơ gia đình & Xác minh PHI</span>
                          </span>
                          <ChevronRight className="h-4 w-4 text-sub" aria-hidden="true" />
                        </Link>
                      </li>
                      <li>
                        <Link
                          to="/patient/identity-claims"
                          className="flex items-center justify-between rounded-xl border border-border bg-canvas px-4 py-3 text-xs font-semibold text-navy transition-colors hover:border-sky-200 hover:bg-sky-50/50"
                        >
                          <span className="flex items-center gap-2.5">
                            <Link2 className="h-4 w-4 text-primary" aria-hidden="true" />
                            <span>Liên kết hồ sơ khám bệnh đã có</span>
                          </span>
                          <ChevronRight className="h-4 w-4 text-sub" aria-hidden="true" />
                        </Link>
                      </li>
                      <li>
                        <Link
                          to="/emr"
                          className="flex items-center justify-between rounded-xl border border-border bg-canvas px-4 py-3 text-xs font-semibold text-navy transition-colors hover:border-sky-200 hover:bg-sky-50/50"
                        >
                          <span className="flex items-center gap-2.5">
                            <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
                            <span>Hồ sơ bệnh án điện tử (EMR)</span>
                          </span>
                          <ChevronRight className="h-4 w-4 text-sub" aria-hidden="true" />
                        </Link>
                      </li>
                      <li>
                        <Link
                          to="/patient/prescriptions"
                          className="flex items-center justify-between rounded-xl border border-border bg-canvas px-4 py-3 text-xs font-semibold text-navy transition-colors hover:border-sky-200 hover:bg-sky-50/50"
                        >
                          <span className="flex items-center gap-2.5">
                            <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
                            <span>Đơn thuốc ngoại trú của tôi</span>
                          </span>
                          <ChevronRight className="h-4 w-4 text-sub" aria-hidden="true" />
                        </Link>
                      </li>
                    </>
                  )}

                  {isStaff && (
                    <li>
                      <Link
                        to={workbenchRoute}
                        className="flex items-center justify-between rounded-xl border border-sky-200 bg-sky-50 px-4 py-3 text-xs font-semibold text-primary transition-colors hover:bg-sky-100"
                      >
                        <span className="flex items-center gap-2.5">
                          <LayoutDashboard className="h-4 w-4" aria-hidden="true" />
                          <span>Mở Bàn làm việc ({roleLabel})</span>
                        </span>
                        <ChevronRight className="h-4 w-4" aria-hidden="true" />
                      </Link>
                    </li>
                  )}
                </ul>
              </div>
            </aside>
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
