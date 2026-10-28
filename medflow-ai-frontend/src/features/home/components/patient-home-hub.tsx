import { Link } from 'react-router-dom';
import { Calendar, RefreshCw, ShieldCheck, Users } from 'lucide-react';

import type { UserDto } from '@/features/auth/types/auth.types';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui';

import { PATIENT_QUICK_ACTIONS } from '../data/patient-home-mock';
import { usePatientHomeSummary } from '../hooks/use-patient-home';
import type { PatientQuickAction } from '../types/home.types';
import { UpcomingAppointmentsCard } from './upcoming-appointments-card';

interface PatientHomeHubProps {
  user: UserDto | null;
}

const ACCENT_CLASSES: Record<PatientQuickAction['accent'], string> = {
  sky: 'border-sky-200 bg-sky-50/70 text-sky-700 hover:border-sky-400',
  emerald: 'border-emerald-200 bg-emerald-50/70 text-emerald-700 hover:border-emerald-400',
  indigo: 'border-indigo-200 bg-indigo-50/70 text-indigo-700 hover:border-indigo-400',
  amber: 'border-amber-200 bg-amber-50/70 text-amber-700 hover:border-amber-400',
};

const METRIC_ITEMS = [
  { key: 'upcomingAppointmentsCount' as const, label: 'Lịch hẹn sắp tới', color: 'text-sky-700' },
  { key: 'familyMembersCount' as const, label: 'Hồ sơ người thân', color: 'text-slate-900' },
  { key: 'completedVisitsCount' as const, label: 'Lượt khám hoàn tất', color: 'text-emerald-700' },
  { key: 'activePrescriptionsCount' as const, label: 'Đơn thuốc còn hạn', color: 'text-amber-600' },
];

/** Bảng điều khiển khởi đầu sau đăng nhập dành riêng cho vai trò PATIENT (M1-009). */
export function PatientHomeHub({ user }: PatientHomeHubProps) {
  const { data, isPending, isError, error, refetch } = usePatientHomeSummary(true);
  const displayName = user?.fullName || data?.fullName || 'Bệnh nhân';
  const patientCode = user?.patientCode || data?.patientCode || 'PAT-MỚI';

  return (
    <section
      aria-label="Bảng điều khiển Bệnh nhân"
      className="border-b border-slate-200 bg-gradient-to-r from-sky-50/80 via-white to-emerald-50/50 py-8"
    >
      <div className="mx-auto max-w-7xl space-y-6 px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col justify-between gap-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-xs md:flex-row md:items-center">
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-0.5 text-xs font-semibold text-emerald-700">
                <ShieldCheck className="h-3.5 w-3.5" />
                Cổng Bệnh nhân • Vai trò được cấp quyền
              </span>
              <span className="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-xs font-semibold text-slate-700">
                Mã BN: {patientCode}
              </span>
            </div>
            <h2 className="text-xl font-bold tracking-tight text-slate-900 sm:text-2xl">
              Xin chào, {displayName}
            </h2>
            <p className="text-xs text-slate-500">
              Quản lý lịch hẹn khám ngoại trú, hồ sơ cá nhân và hồ sơ người thân tại một nơi duy
              nhất.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <a
              href="#booking"
              className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-4 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-sky-700"
            >
              <Calendar className="h-4 w-4" />
              Đặt lịch khám mới
            </a>
            <Link
              to="/profile"
              className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-xs font-semibold text-slate-700 transition-colors hover:bg-slate-50"
            >
              Hồ sơ cá nhân
            </Link>
            <Link
              to="/family"
              className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-xs font-semibold text-slate-700 transition-colors hover:bg-slate-50"
            >
              <Users className="h-4 w-4 text-sky-600" />
              Hồ sơ người thân
            </Link>
          </div>
        </div>

        {isPending && (
          <div
            role="status"
            className="rounded-2xl border border-slate-200 bg-white p-6 text-center text-xs font-medium text-slate-500"
          >
            Đang tải thông tin bảng điều khiển bệnh nhân...
          </div>
        )}

        {isError && (
          <Alert tone="error" className="flex items-center justify-between gap-4">
            <span>
              {getErrorMessage(
                error,
                'Không thể tải dữ liệu tổng quan bệnh nhân. Vui lòng thử lại.',
              )}
            </span>
            <button
              type="button"
              onClick={() => void refetch()}
              className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 hover:bg-rose-50"
            >
              <RefreshCw className="h-3.5 w-3.5" />
              Thử lại
            </button>
          </Alert>
        )}

        {data && !isPending && !isError && (
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <div className="space-y-4 lg:col-span-7">
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                {METRIC_ITEMS.map((metric) => (
                  <div
                    key={metric.key}
                    className="rounded-xl border border-slate-200 bg-white p-3.5"
                  >
                    <span className="text-[11px] font-medium text-slate-500">{metric.label}</span>
                    <div className={`mt-1 text-xl font-bold ${metric.color}`}>
                      {data.metrics[metric.key]}
                    </div>
                  </div>
                ))}
              </div>

              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                {PATIENT_QUICK_ACTIONS.map((item) => {
                  const className = `rounded-2xl border p-4 transition-all ${ACCENT_CLASSES[item.accent]}`;
                  const content = (
                    <>
                      <div className="flex items-center justify-between gap-2">
                        <span className="text-sm font-bold text-slate-900">{item.label}</span>
                        {item.badgeText && (
                          <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-slate-700 shadow-2xs">
                            {item.badgeText}
                          </span>
                        )}
                      </div>
                      <p className="mt-1 text-xs text-slate-600">{item.description}</p>
                    </>
                  );
                  return item.href.startsWith('#') ? (
                    <a key={item.id} href={item.href} className={className}>
                      {content}
                    </a>
                  ) : (
                    <Link key={item.id} to={item.href} className={className}>
                      {content}
                    </Link>
                  );
                })}
              </div>
            </div>

            <UpcomingAppointmentsCard appointments={data.upcomingAppointments} />
          </div>
        )}
      </div>
    </section>
  );
}
