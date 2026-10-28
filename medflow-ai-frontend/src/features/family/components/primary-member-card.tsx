import { Link } from 'react-router-dom';
import { Calendar, Eye, Mail, Phone, Shield } from 'lucide-react';
import type { FamilyMember } from '../types/family.types';

interface PrimaryMemberCardProps {
  member: FamilyMember;
  onBookAppointment?: (member: FamilyMember) => void;
}

export function PrimaryMemberCard({ member, onBookAppointment }: PrimaryMemberCardProps) {
  return (
    <div className="relative flex flex-col justify-between rounded-2xl border-2 border-sky-400 bg-white p-5 shadow-xs">
      <div>
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-slate-100 pb-4">
          <div className="flex items-center gap-3.5">
            <div className="relative h-13 w-13 shrink-0">
              <img
                src={member.avatarUrl}
                alt={member.fullName}
                className="h-full w-full rounded-full border-2 border-sky-200 object-cover"
              />
              <span className="absolute bottom-0 right-0 flex h-4.5 w-4.5 items-center justify-center rounded-full bg-sky-600 text-white shadow-2xs">
                <Shield className="h-2.5 w-2.5" />
              </span>
            </div>

            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-sm font-bold tracking-wide text-slate-900">
                  {member.fullName}
                </h3>
                <span className="rounded-md bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-700">
                  {member.relationship}
                </span>
              </div>
              <p className="mt-1 text-xs text-slate-400">{member.subStatus}</p>
            </div>
          </div>

          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-600" />
            Chủ tài khoản chính
          </span>
        </div>

        {/* Details list */}
        <div className="mt-4 space-y-2.5 text-xs text-slate-600">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-2 text-slate-400">
              <Calendar className="h-3.5 w-3.5 text-slate-400" />
              Ngày sinh & Tuổi:
            </span>
            <span className="font-medium text-slate-900">
              {member.birthDate} ({member.age} tuổi) • {member.gender}
            </span>
          </div>

          {member.phone && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <Phone className="h-3.5 w-3.5 text-slate-400" />
                Số điện thoại:
              </span>
              <span className="font-mono font-medium text-slate-900">{member.phone}</span>
            </div>
          )}

          {member.email && (
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-slate-400">
                <Mail className="h-3.5 w-3.5 text-slate-400" />
                Email liên hệ:
              </span>
              <span className="font-medium text-slate-900">{member.email}</span>
            </div>
          )}
        </div>

        {/* Vitals block */}
        {member.vitals && (
          <div className="mt-4 grid grid-cols-3 gap-2 rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
            <div>
              <span className="block text-[10px] font-medium text-slate-400">NHÓM MÁU</span>
              <span className="mt-0.5 block text-sm font-bold text-red-600">
                {member.vitals.bloodType}
              </span>
            </div>
            <div>
              <span className="block text-[10px] font-medium text-slate-400">CHIỀU CAO</span>
              <span className="mt-0.5 block text-sm font-bold text-slate-800">
                {member.vitals.heightCm} cm
              </span>
            </div>
            <div>
              <span className="block text-[10px] font-medium text-slate-400">CÂN NẶNG</span>
              <span className="mt-0.5 block text-sm font-bold text-slate-800">
                {member.vitals.weightKg} kg
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Buttons */}
      <div className="mt-5 flex items-center gap-2.5 pt-3">
        <button
          type="button"
          onClick={() => onBookAppointment?.(member)}
          className="flex flex-1 items-center justify-center gap-1.5 rounded-xl bg-sky-600 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
        >
          <Calendar className="h-3.5 w-3.5" />
          Đặt khám ngay
        </button>
        <Link
          to="/profile"
          className="flex flex-1 items-center justify-center gap-1.5 rounded-xl border border-slate-200 bg-slate-50 py-2.5 text-xs font-bold text-slate-700 transition-colors hover:bg-slate-100 active:scale-[0.99]"
        >
          <Eye className="h-3.5 w-3.5 text-slate-500" />
          Xem hồ sơ chi tiết
        </Link>
      </div>
    </div>
  );
}
