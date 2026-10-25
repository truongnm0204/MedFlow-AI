import { Link } from 'react-router-dom';
import {
  AlertCircle,
  Bell,
  Calendar,
  Eye,
  FileText,
  Heart,
  Pencil,
  Phone,
  Stethoscope,
  User,
  Users,
} from 'lucide-react';

import type { FamilyMember } from '../types/family.types';

interface DependentMemberCardProps {
  member: FamilyMember;
  onBookAppointment?: (member: FamilyMember) => void;
  onEditMember?: (member: FamilyMember) => void;
}

export function DependentMemberCard({
  member,
  onBookAppointment,
  onEditMember,
}: DependentMemberCardProps) {
  const getBadgeStyle = () => {
    switch (member.relationshipBadgeColor) {
      case 'blue':
        return 'bg-blue-50 text-blue-700 border-blue-200/60';
      case 'rose':
        return 'bg-rose-50 text-rose-700 border-rose-200/60';
      case 'amber':
        return 'bg-amber-50 text-amber-700 border-amber-200/60';
      default:
        return 'bg-slate-100 text-slate-700 border-slate-200/60';
    }
  };

  return (
    <div className="relative flex flex-col justify-between rounded-2xl border border-slate-200/90 bg-white p-5 shadow-xs">
      <div>
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-slate-100 pb-4">
          <div className="flex items-center gap-3.5">
            <div className="relative h-13 w-13 shrink-0">
              <img
                src={member.avatarUrl}
                alt={member.fullName}
                className="h-full w-full rounded-full border border-slate-200 object-cover"
              />
              <span className="absolute right-0 bottom-0 flex h-4.5 w-4.5 items-center justify-center rounded-full bg-slate-100 text-slate-500 shadow-2xs">
                {member.relationship === 'Con trai' ? (
                  <User className="h-2.5 w-2.5 text-sky-600" />
                ) : member.relationship === 'Vợ' ? (
                  <Heart className="h-2.5 w-2.5 text-rose-500" />
                ) : (
                  <Users className="h-2.5 w-2.5 text-amber-600" />
                )}
              </span>
            </div>

            <div>
              <div className="flex items-center gap-2">
                <Link
                  to={`/family/${member.id}`}
                  className="text-sm font-bold tracking-wide text-slate-900 transition-colors hover:text-sky-600"
                >
                  {member.fullName}
                </Link>
                <span
                  className={`rounded-md border px-2 py-0.5 text-[11px] font-semibold ${getBadgeStyle()}`}
                >
                  {member.relationship}
                </span>
              </div>
              <div className="mt-1 flex items-center gap-2 text-xs">
                <span className="font-mono font-semibold text-sky-600">{member.patientCode}</span>
                <span className="text-slate-300">•</span>
                <span className="text-emerald-600">{member.subStatus}</span>
              </div>
            </div>
          </div>

          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200/80 bg-sky-50 px-2.5 py-1 text-xs font-semibold text-sky-700">
            <span className="h-1.5 w-1.5 rounded-full bg-sky-600" />
            Đã liên kết
          </span>
        </div>

        {/* Details list */}
        <div className="mt-4 space-y-2.5 text-xs text-slate-600">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-2 text-slate-400">
              <Calendar className="h-3.5 w-3.5 text-slate-400" />
              Ngày sinh & Giới tính:
            </span>
            <span className="font-medium text-slate-900">
              {member.birthDate} ({member.age} tuổi) • {member.gender}
            </span>
          </div>

          {member.guardian && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <User className="h-3.5 w-3.5 text-slate-400" />
                Người giám hộ:
              </span>
              <span className="font-medium text-slate-900">{member.guardian}</span>
            </div>
          )}

          {member.phone && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <Phone className="h-3.5 w-3.5 text-slate-400" />
                Số điện thoại liên hệ:
              </span>
              <span className="font-mono font-medium text-slate-900">{member.phone}</span>
            </div>
          )}

          {member.emergencyPhone && !member.phone && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <Phone className="h-3.5 w-3.5 text-slate-400" />
                SĐT liên hệ khẩn cấp:
              </span>
              <span className="font-mono font-medium text-slate-900">{member.emergencyPhone}</span>
            </div>
          )}

          {member.latestVisit && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <Stethoscope className="h-3.5 w-3.5 text-slate-400" />
                Khám gần nhất:
              </span>
              <span className="font-medium text-slate-900">{member.latestVisit}</span>
            </div>
          )}

          {member.clinicalNotes && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <AlertCircle className="h-3.5 w-3.5 text-slate-400" />
                Lưu ý khám:
              </span>
              <span className="font-medium text-slate-900">{member.clinicalNotes}</span>
            </div>
          )}
        </div>

        {/* Extra info for Wife (two boxes) */}
        {member.followUpSchedule && member.relationship === 'Vợ' && (
          <div className="mt-4 grid grid-cols-2 gap-2 text-xs">
            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5">
              <span className="flex items-center gap-1.5 text-[11px] font-medium text-slate-400">
                <Calendar className="h-3 w-3 text-slate-400" />
                Lịch tái khám
              </span>
              <span className="mt-1 block font-semibold text-slate-800">
                {member.followUpSchedule}
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5">
              <span className="flex items-center gap-1.5 text-[11px] font-medium text-slate-400">
                <FileText className="h-3 w-3 text-slate-400" />
                Gói dịch vụ
              </span>
              <span className="mt-1 block font-semibold text-slate-800">
                {member.servicePackage}
              </span>
            </div>
          </div>
        )}

        {/* Notice alert banner if exists */}
        {member.notice && (
          <div
            className={`mt-4 flex items-start gap-2 rounded-xl p-2.5 text-xs ${
              member.notice.type === 'warning'
                ? 'border border-amber-200/80 bg-amber-50/70 text-amber-800'
                : 'border border-sky-200/80 bg-sky-50/70 text-sky-800'
            }`}
          >
            {member.notice.type === 'warning' ? (
              <Stethoscope className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-600" />
            ) : (
              <Bell className="mt-0.5 h-3.5 w-3.5 shrink-0 text-sky-600" />
            )}
            <p className="leading-relaxed">{member.notice.message}</p>
          </div>
        )}
      </div>

      {/* Buttons */}
      <div className="mt-5 flex items-center gap-2 pt-3">
        <button
          type="button"
          onClick={() => onBookAppointment?.(member)}
          className="flex flex-1 items-center justify-center gap-1.5 rounded-xl bg-sky-600 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
        >
          <Calendar className="h-3.5 w-3.5" />
          Đặt khám ngay
        </button>

        <button
          type="button"
          onClick={() => onEditMember?.(member)}
          className="flex items-center justify-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50 active:scale-[0.99]"
        >
          <Pencil className="h-3.5 w-3.5 text-slate-500" />
          Sửa thông tin
        </button>

        <Link
          to={`/family/${member.id}`}
          className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-500 transition-colors hover:bg-slate-50 hover:text-sky-600"
          aria-label={`Xem chi tiết ${member.fullName}`}
          title="Xem chi tiết hồ sơ"
        >
          <Eye className="h-4 w-4" />
        </Link>
      </div>
    </div>
  );
}
