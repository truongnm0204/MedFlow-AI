import {
  Check,
  Edit2,
  Lock,
  Mail,
  MapPin,
  Phone,
  PhoneCall,
  Stethoscope,
  UserCheck,
} from 'lucide-react';

import type { FamilyMember } from '../types/family.types';

interface FamilyMemberContactCardProps {
  member: FamilyMember;
}

export function FamilyMemberContactCard({ member }: FamilyMemberContactCardProps) {
  const canManage = member.permissions?.canManage ?? true;
  const canViewMedical = member.permissions?.canViewMedicalRecords ?? true;

  return (
    <div className="mt-6 rounded-2xl border border-slate-200/90 bg-white p-6 shadow-xs">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-5">
        <div className="flex items-center gap-3.5">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-sky-600">
            <UserCheck className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-base font-bold tracking-tight text-slate-900">
              Thông tin liên hệ & tóm tắt y tế
            </h2>
            <p className="mt-0.5 text-xs text-slate-500">
              Kênh nhận thông báo lịch hẹn, người giám hộ & thông tin khám ngoại trú
            </p>
          </div>
        </div>

        {canManage && (
          <button
            type="button"
            className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3.5 py-1.5 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50"
          >
            <Edit2 className="h-3.5 w-3.5 text-slate-500" />
            Cập nhật
          </button>
        )}
      </div>

      {/* Row 1: Phone & Email */}
      <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-1.5 text-xs font-semibold tracking-wider text-slate-500 uppercase">
              <Phone className="h-3.5 w-3.5" />
              Số điện thoại liên hệ
            </span>
            <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200/70 bg-emerald-50 px-2 py-0.5 text-xs font-semibold text-emerald-700">
              <Check className="h-3 w-3 stroke-[2.5]" />
              Nhận SMS lịch hẹn
            </span>
          </div>
          <p className="mt-2 font-mono text-base font-semibold tracking-wide text-slate-900">
            {member.phone ?? member.emergencyPhone ?? 'Dùng SĐT chủ tài khoản'}
          </p>
        </div>

        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-1.5 text-xs font-semibold tracking-wider text-slate-500 uppercase">
              <Mail className="h-3.5 w-3.5" />
              Email nhận kết quả
            </span>
          </div>
          <p className="mt-2 text-base font-semibold text-slate-900">
            {member.email ?? 'Nhận qua email chủ tài khoản chính'}
          </p>
        </div>
      </div>

      {/* Row 2: Address */}
      <div className="mt-4 rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
        <span className="flex items-center gap-1.5 text-xs font-semibold tracking-wider text-slate-500 uppercase">
          <MapPin className="h-3.5 w-3.5 text-slate-400" />
          Địa chỉ thường trú hiện tại
        </span>
        <p className="mt-2 text-sm leading-relaxed font-semibold text-slate-900">
          {member.address ?? 'Cùng địa chỉ thường trú với chủ tài khoản chính'}
        </p>
      </div>

      {/* Subsection: Clinical Visit Summary */}
      <div className="mt-5 rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Stethoscope className="h-4 w-4 text-sky-600" />
            <h3 className="text-xs font-bold tracking-wider text-slate-800 uppercase">
              Tóm tắt khám ngoại trú & theo dõi
            </h3>
          </div>
          <span className="rounded-md border border-sky-200/70 bg-sky-50 px-2.5 py-0.5 text-xs font-semibold text-sky-700">
            {member.servicePackage ?? 'Khám ngoại trú tiêu chuẩn'}
          </span>
        </div>

        {canViewMedical ? (
          <div className="mt-3 grid grid-cols-1 gap-3 rounded-xl border border-slate-200/80 bg-white p-3.5 sm:grid-cols-3">
            <div>
              <span className="block text-xs font-medium text-slate-500">
                Lần khám gần nhất
              </span>
              <span className="mt-1 block text-sm font-semibold text-slate-900">
                {member.latestVisit ?? 'Chưa có lượt khám'}
              </span>
            </div>

            <div>
              <span className="block text-xs font-medium text-slate-500">
                Lịch tái khám
              </span>
              <span className="mt-1 block text-sm font-semibold text-slate-900">
                {member.followUpSchedule ?? 'Chưa đặt lịch mới'}
              </span>
            </div>

            <div>
              <span className="block text-xs font-medium text-slate-500">
                Lưu ý lâm sàng
              </span>
              <span className="mt-1 block text-sm font-semibold text-slate-900">
                {member.clinicalNotes ?? 'Không có lưu ý đặc biệt'}
              </span>
            </div>
          </div>
        ) : (
          <div className="mt-3 flex items-center gap-2 rounded-xl border border-slate-200 bg-white p-3.5 text-xs text-slate-500">
            <Lock className="h-4 w-4 shrink-0 text-slate-400" />
            <span>
              Quyền xem chi tiết bệnh án ngoại trú của thành viên này hiện đang bị giới hạn theo cài
              đặt ủy quyền.
            </span>
          </div>
        )}
      </div>

      {/* Subsection: Emergency Contact / Guardian */}
      <div className="mt-5 rounded-xl border border-rose-100 bg-rose-50/30 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <PhoneCall className="h-4 w-4 text-rose-500" />
            <h3 className="text-xs font-bold tracking-wider text-slate-800 uppercase">
              Người giám hộ / Liên hệ khẩn cấp (Emergency Contact)
            </h3>
          </div>
          <span className="rounded-md border border-sky-200/70 bg-sky-50 px-2.5 py-0.5 text-xs font-semibold text-sky-700">
            Bắt buộc theo y tế số
          </span>
        </div>

        <div className="mt-3 grid grid-cols-1 gap-3 rounded-xl border border-slate-200/80 bg-white p-3.5 sm:grid-cols-3">
          <div>
            <span className="block text-xs font-medium text-slate-500">Họ và tên</span>
            <span className="mt-1 block text-sm font-semibold text-slate-900">
              {member.emergencyContactName ?? member.guardian ?? 'Nguyễn Văn An'}
            </span>
          </div>

          <div>
            <span className="block text-xs font-medium text-slate-500">Mối quan hệ</span>
            <span className="mt-1 inline-flex rounded-md border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-xs font-semibold text-sky-700">
              {member.emergencyContactRelation ?? 'Chủ tài khoản chính'}
            </span>
          </div>

          <div>
            <span className="block text-xs font-medium text-slate-500">
              Số điện thoại khẩn cấp
            </span>
            <span className="mt-1 block font-mono text-sm font-semibold text-slate-900">
              {member.emergencyPhone ?? member.phone ?? '0912 345 678'}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
