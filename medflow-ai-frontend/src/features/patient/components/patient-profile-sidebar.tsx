import { Link } from 'react-router-dom';
import {
  AlertCircle,
  Calendar,
  Check,
  FileText,
  Lock,
  Plus,
  ShieldCheck,
  Users,
} from 'lucide-react';
import type { PatientProfile } from '../types/patient-profile.types';

interface PatientProfileSidebarProps {
  profile: PatientProfile;
  onOpenBookingModal?: () => void;
}

function getInitials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return 'BN';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

export function PatientProfileSidebar({ profile, onOpenBookingModal }: PatientProfileSidebarProps) {
  const { identity, vitals, shortcuts } = profile;

  return (
    <div className="space-y-5">
      <div className="overflow-hidden rounded-2xl border border-border bg-white shadow-xs">
        <div className="h-2 w-full bg-gradient-to-r from-sky-500 via-blue-600 to-indigo-600" />

        <div className="p-6 text-center">
          <div className="relative mx-auto h-28 w-28">
            {profile.avatarUrl ? (
              <img
                src={profile.avatarUrl}
                alt={identity.fullName}
                className="h-full w-full rounded-full border-4 border-slate-100 object-cover shadow-xs"
              />
            ) : (
              <div
                aria-label={identity.fullName}
                className="flex h-full w-full items-center justify-center rounded-full border-4 border-slate-100 bg-sky-50 text-2xl font-bold text-primary shadow-xs"
              >
                {getInitials(identity.fullName)}
              </div>
            )}
            {identity.isIdentityVerified && (
              <span
                className="absolute bottom-0 right-0 flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-emerald-500 text-white shadow-xs"
                title="Đã xác thực CCCD"
              >
                <Check className="h-4 w-4" aria-hidden="true" />
              </span>
            )}
          </div>

          <h2 className="mt-3 text-lg font-bold tracking-wide text-ink">{identity.fullName}</h2>

          <div className="mt-2 flex justify-center">
            {identity.isIdentityVerified ? (
              <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200/70 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700">
                <Check className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Đã xác thực danh tính (CCCD)</span>
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-semibold text-amber-800">
                <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Chưa xác thực CCCD</span>
              </span>
            )}
          </div>

          <div className="mt-6 grid grid-cols-3 gap-2.5">
            <div className="rounded-xl border border-slate-100 bg-canvas p-2.5 text-center">
              <span className="block text-[11px] font-medium text-sub">Nhóm máu</span>
              <span
                className="mt-0.5 block text-sm font-bold text-red-600"
                title="Thông tin nhóm máu do bệnh nhân tự khai báo"
              >
                {vitals.bloodType}
              </span>
              {vitals.bloodType !== 'Chưa cập nhật' && (
                <span className="block text-[10px] text-sub">(tự khai báo)</span>
              )}
            </div>

            <div className="rounded-xl border border-slate-100 bg-canvas p-2.5 text-center">
              <span className="block text-[11px] font-medium text-sub">Chiều cao</span>
              <span className="mt-0.5 block text-sm font-bold text-ink">
                {vitals.heightCm != null ? `${vitals.heightCm} cm` : 'Chưa cập nhật'}
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-canvas p-2.5 text-center">
              <span className="block text-[11px] font-medium text-sub">Cân nặng</span>
              <span className="mt-0.5 block text-sm font-bold text-ink">
                {vitals.weightKg != null ? `${vitals.weightKg} kg` : 'Chưa cập nhật'}
              </span>
            </div>
          </div>

          <div className="mt-7 text-left">
            <h3 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Lối tắt quản lý
            </h3>

            <div className="mt-3 space-y-2">
              <Link
                to="/family"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-border hover:bg-canvas"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-primary">
                    <Users className="h-4 w-4" aria-hidden="true" />
                  </div>
                  <span className="text-xs font-semibold text-ink">Hồ sơ người thân</span>
                </div>
                <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-sub">
                  {shortcuts.familyMembersCount != null
                    ? `${shortcuts.familyMembersCount} người`
                    : 'Xem danh sách'}
                </span>
              </Link>

              <Link
                to="/patient/visits"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-border hover:bg-canvas"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-primary">
                    <Calendar className="h-4 w-4" aria-hidden="true" />
                  </div>
                  <span className="text-xs font-semibold text-ink">Lịch sử khám ngoại trú</span>
                </div>
                <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-sub">
                  {shortcuts.outpatientVisitsCount != null
                    ? `${shortcuts.outpatientVisitsCount} lượt khám`
                    : 'Xem lịch sử'}
                </span>
              </Link>

              <Link
                to="/appointments"
                className="flex items-center justify-between rounded-xl border border-slate-100 p-2.5 transition-colors hover:border-border hover:bg-canvas"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-50 text-primary">
                    <FileText className="h-4 w-4" aria-hidden="true" />
                  </div>
                  <span className="text-xs font-semibold text-ink">Lịch hẹn &amp; Đơn thuốc</span>
                </div>
                <span className="rounded-full border border-emerald-200/70 bg-emerald-50 px-2.5 py-0.5 text-xs font-medium text-emerald-700">
                  {shortcuts.activePrescriptionsCount != null
                    ? `${shortcuts.activePrescriptionsCount} đơn còn hạn`
                    : 'Tra cứu'}
                </span>
              </Link>
            </div>
          </div>

          <div className="mt-5 flex items-start gap-2.5 rounded-xl border border-border bg-canvas p-3 text-left">
            <Lock className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <p className="text-[11px] leading-relaxed text-sub">
              Mã hồ sơ bệnh nhân điện tử (PID) được liên kết duy nhất với CCCD gốc và lưu trữ mã hóa
              bảo mật.
            </p>
          </div>
        </div>
      </div>

      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-blue-600 via-blue-700 to-indigo-800 p-5 text-white shadow-sm">
        <div className="inline-flex items-center gap-1.5 rounded-md bg-white/15 px-2.5 py-1 text-[11px] font-semibold text-white backdrop-blur-xs">
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Dịch vụ Khám Tự Nguyện</span>
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
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Đặt lịch khám mới</span>
        </button>
      </div>
    </div>
  );
}
