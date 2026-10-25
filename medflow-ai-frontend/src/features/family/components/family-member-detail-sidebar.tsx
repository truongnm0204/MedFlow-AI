import { Link } from 'react-router-dom';
import { Calendar, Check, FileText, Lock, Plus, ShieldCheck, XCircle } from 'lucide-react';

import type { FamilyMember } from '../types/family.types';

interface FamilyMemberDetailSidebarProps {
  member: FamilyMember;
  onBookAppointment?: (member: FamilyMember) => void;
}

export function FamilyMemberDetailSidebar({
  member,
  onBookAppointment,
}: FamilyMemberDetailSidebarProps) {
  const vitals = member.vitals ?? { bloodType: '--', heightCm: 0, weightKg: 0 };
  const permissions = member.permissions ?? {
    canManage: true,
    canBookAppointments: true,
    canViewMedicalRecords: true,
    relationshipStatus: 'ACTIVE',
  };
  const canBook =
    permissions.canBookAppointments && permissions.relationshipStatus === 'ACTIVE';

  return (
    <div className="space-y-5">
      {/* Member Overview Card */}
      <div className="overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-xs">
        {/* Top Gradient Banner matching Personal Profile */}
        <div className="h-2 w-full bg-gradient-to-r from-sky-500 via-blue-600 to-indigo-600" />

        <div className="p-6 text-center">
          <div className="relative mx-auto h-28 w-28">
            <img
              src={member.avatarUrl}
              alt={member.fullName}
              className="h-full w-full rounded-full border-4 border-slate-100 object-cover shadow-xs"
            />
            {member.isIdentityVerified !== false && (
              <span
                className="absolute right-0 bottom-0 flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-emerald-500 text-white shadow-xs"
                title="Đã xác minh hồ sơ người thân"
              >
                <Check className="h-4 w-4 stroke-[3]" />
              </span>
            )}
          </div>

          <h2 className="mt-3 text-lg font-bold tracking-tight text-slate-900">
            {member.fullName}
          </h2>

          <div className="mt-2 flex flex-wrap items-center justify-center gap-2 text-xs">
            <span className="rounded-md border border-sky-200/80 bg-sky-50 px-2.5 py-0.5 font-semibold text-sky-700">
              {member.relationship}
            </span>
            <span className="font-mono font-semibold text-sky-600">{member.patientCode}</span>
          </div>

          <p className="mt-1.5 text-xs text-slate-500">{member.subStatus}</p>

          {/* 3 Vitals Boxes */}
          <div className="mt-6 grid grid-cols-3 gap-2.5">
            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-xs font-medium text-slate-500">Nhóm máu</span>
              <span className="mt-1 block text-base font-bold text-red-600">
                {vitals.bloodType}
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-xs font-medium text-slate-500">Chiều cao</span>
              <span className="mt-1 block text-base font-bold text-slate-800">
                {vitals.heightCm > 0 ? `${vitals.heightCm} cm` : '--'}
              </span>
            </div>

            <div className="rounded-xl border border-slate-100 bg-slate-50/70 p-2.5 text-center">
              <span className="block text-xs font-medium text-slate-500">Cân nặng</span>
              <span className="mt-1 block text-base font-bold text-slate-800">
                {vitals.weightKg > 0 ? `${vitals.weightKg} kg` : '--'}
              </span>
            </div>
          </div>

          {/* Delegated Permissions Section */}
          <div className="mt-7 text-left">
            <h3 className="text-xs font-bold tracking-wider text-slate-500 uppercase">
              Phạm vi ủy quyền y tế
            </h3>

            <div className="mt-3 space-y-2 rounded-xl border border-slate-100 bg-slate-50/70 p-3.5 text-xs">
              <div className="flex items-center justify-between">
                <span className="text-slate-600">Đặt lịch khám hộ</span>
                {permissions.canBookAppointments ? (
                  <span className="inline-flex items-center gap-1 font-semibold text-emerald-700">
                    <Check className="h-3.5 w-3.5" /> Được phép
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 font-medium text-slate-400">
                    <XCircle className="h-3.5 w-3.5" /> Giới hạn
                  </span>
                )}
              </div>

              <div className="flex items-center justify-between">
                <span className="text-slate-600">Xem hồ sơ & kết quả khám</span>
                {permissions.canViewMedicalRecords ? (
                  <span className="inline-flex items-center gap-1 font-semibold text-emerald-700">
                    <Check className="h-3.5 w-3.5" /> Được phép
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 font-medium text-slate-400">
                    <XCircle className="h-3.5 w-3.5" /> Giới hạn
                  </span>
                )}
              </div>

              <div className="flex items-center justify-between">
                <span className="text-slate-600">Cập nhật thông tin hồ sơ</span>
                {permissions.canManage ? (
                  <span className="inline-flex items-center gap-1 font-semibold text-emerald-700">
                    <Check className="h-3.5 w-3.5" /> Được phép
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 font-medium text-slate-400">
                    <XCircle className="h-3.5 w-3.5" /> Chỉ đọc
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* Quick Shortcuts */}
          <div className="mt-7 text-left">
            <h3 className="text-xs font-bold tracking-wider text-slate-500 uppercase">
              Lối tắt quản lý
            </h3>

            <div className="mt-3 space-y-2">
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
                  {member.outpatientVisitsCount ?? 0} lượt khám
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
                  {member.activePrescriptionsCount ?? 0} đơn còn hạn
                </span>
              </Link>
            </div>
          </div>

          <div className="mt-5 flex items-start gap-2.5 rounded-xl border border-slate-200/80 bg-slate-50/80 p-3 text-left">
            <Lock className="mt-0.5 h-4 w-4 shrink-0 text-sky-600" />
            <p className="text-xs leading-relaxed text-slate-500">
              Hồ sơ người thân được liên kết theo chuẩn HL7/FHIR. Mọi thao tác đặt lịch đều được ghi
              nhận nhật ký bảo mật.
            </p>
          </div>
        </div>
      </div>

      {/* Blue Booking Promo Banner */}
      {canBook && (
        <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-blue-600 via-blue-700 to-indigo-800 p-5 text-white shadow-sm">
          <div className="inline-flex items-center gap-1.5 rounded-md bg-white/15 px-2.5 py-1 text-xs font-semibold text-white backdrop-blur-xs">
            <ShieldCheck className="h-3.5 w-3.5" />
            Đặt lịch hộ người thân
          </div>

          <h3 className="mt-3 text-base font-bold text-white">Cần đặt lịch hẹn mới?</h3>
          <p className="mt-1 text-xs leading-relaxed text-blue-100/90">
            Sử dụng hồ sơ của {member.fullName} để đặt lịch khám chuyên khoa nhanh chóng.
          </p>

          <button
            type="button"
            onClick={() => onBookAppointment?.(member)}
            className="mt-4 flex w-full items-center justify-center gap-1.5 rounded-xl bg-white py-2.5 text-xs font-bold text-blue-700 shadow-sm transition-all hover:bg-blue-50 active:scale-[0.99]"
          >
            <Plus className="h-3.5 w-3.5 stroke-[3]" />
            Đặt lịch khám cho thành viên này
          </button>
        </div>
      )}
    </div>
  );
}
