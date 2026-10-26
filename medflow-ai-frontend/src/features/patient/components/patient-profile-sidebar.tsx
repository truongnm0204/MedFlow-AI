import { Link } from 'react-router-dom';
import { Calendar, Check, FileText, Lock, Plus, ShieldCheck, Users } from 'lucide-react';
import type { PatientProfile } from '../types/patient-profile.types';

interface PatientProfileSidebarProps {
  profile: PatientProfile;
  onOpenBookingModal?: () => void;
}

export function PatientProfileSidebar({ profile, onOpenBookingModal }: PatientProfileSidebarProps) {
  const { identity, vitals, shortcuts } = profile;

  return (
    <div className="space-y-5">
      {/* Profile Overview Card */}
      <div className="overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-xs">
        {/* Top Gradient Banner */}
        <div className="h-2 w-full bg-gradient-to-r from-sky-500 via-blue-600 to-indigo-600" />

        <div className="p-6 text-center">
          {/* Avatar Container with Verification Badge */}
          <div className="relative mx-auto h-28 w-28">
            <img
              src={profile.avatarUrl}
              alt={identity.fullName}
              className="h-full w-full rounded-full border-4 border-slate-100 object-cover shadow-xs"
            />
            {identity.isIdentityVerified && (
              <span
                className="absolute bottom-0 right-0 flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-emerald-500 text-white shadow-xs"
                title="Đã xác thực CCCD"
              >
                <Check className="h-4 w-4 stroke-[3]" />
              </span>
            )}
          </div>

          {/* Full Name */}
          <h2 className="mt-3 text-lg font-bold tracking-wide text-slate-900">
            {identity.fullName}
          </h2>

          {/* Identity Status Badge */}
          <div className="mt-2 flex justify-center">
            <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200/70 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700">
              <Check className="h-3.5 w-3.5 stroke-[2.5]" />
              Đã xác thực danh tính (CCCD)
            </span>
          </div>

          {/* 3 Metric / Vitals Boxes */}
          <div className="mt-6 grid grid-cols-3 gap-2.5">
            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-[11px] font-medium text-slate-500">Nhóm máu</span>
              <span className="mt-0.5 block text-base font-bold text-red-600">
                {vitals.bloodType}
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-[11px] font-medium text-slate-500">Chiều cao</span>
              <span className="mt-0.5 block text-base font-bold text-slate-800">
                {vitals.heightCm} cm
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-[11px] font-medium text-slate-500">Cân nặng</span>
              <span className="mt-0.5 block text-base font-bold text-slate-800">
                {vitals.weightKg} kg
              </span>
            </div>
          </div>

          {/* Lối tắt quản lý (Quick Shortcuts) */}
          <div className="mt-7 text-left">
            <h3 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Lối tắt quản lý
            </h3>

            <div className="mt-3 space-y-2">
              <Link
                to="/family"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-slate-200 hover:bg-slate-50"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-sky-600">
                    <Users className="h-4 w-4" />
                  </div>
                  <span className="text-xs font-semibold text-slate-700">Hồ sơ người thân</span>
                </div>
                <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600">
                  {shortcuts.familyMembersCount} người
                </span>
              </Link>

              <Link
                to="/appointments"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-slate-200 hover:bg-slate-50"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-sky-600">
                    <Calendar className="h-4 w-4" />
                  </div>
                  <span className="text-xs font-semibold text-slate-700">
                    Lịch sử khám ngoại trú
                  </span>
                </div>
                <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600">
                  {shortcuts.outpatientVisitsCount} lượt khám
                </span>
              </Link>

              <Link
                to="/emr"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-slate-200 hover:bg-slate-50"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-sky-600">
                    <FileText className="h-4 w-4" />
                  </div>
                  <span className="text-xs font-semibold text-slate-700">
                    Đơn thuốc điện tử gần nhất
                  </span>
                </div>
                <span className="rounded-full border border-emerald-200/70 bg-emerald-50 px-2.5 py-0.5 text-xs font-medium text-emerald-700">
                  {shortcuts.activePrescriptionsCount} đơn còn hạn
                </span>
              </Link>
            </div>
          </div>

          {/* Privacy & Security Note Box */}
          <div className="mt-5 flex items-start gap-2.5 rounded-xl border border-slate-200/80 bg-slate-50/80 p-3 text-left">
            <Lock className="mt-0.5 h-4 w-4 shrink-0 text-sky-600" />
            <p className="text-[11px] leading-relaxed text-slate-500">
              Mã hồ sơ bệnh nhân điện tử (PID) được liên kết duy nhất với CCCD gốc và lưu trữ mã hóa
              đầu cuối 256-bit.
            </p>
          </div>
        </div>
      </div>

      {/* Blue Booking Promo Banner */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-blue-600 via-blue-700 to-indigo-800 p-5 text-white shadow-sm">
        <div className="inline-flex items-center gap-1.5 rounded-md bg-white/15 px-2.5 py-1 text-[11px] font-semibold text-white backdrop-blur-xs">
          <ShieldCheck className="h-3.5 w-3.5" />
          Dịch vụ Khám Tự Nguyện
        </div>

        <h3 className="mt-3 text-base font-bold text-white">Cần đặt lịch hẹn mới?</h3>
        <p className="mt-1 text-xs leading-relaxed text-blue-100/90">
          Khám chuyên khoa ngoại trú, không chờ đợi, lựa chọn bác sĩ theo yêu cầu.
        </p>

        <button
          type="button"
          onClick={onOpenBookingModal}
          className="mt-4 flex w-full items-center justify-center gap-1.5 rounded-xl bg-white py-2.5 text-xs font-bold text-blue-700 shadow-sm transition-all hover:bg-blue-50 active:scale-[0.99]"
        >
          <Plus className="h-3.5 w-3.5 stroke-[3]" />
          Đặt lịch khám mới
        </button>
      </div>
    </div>
  );
}
