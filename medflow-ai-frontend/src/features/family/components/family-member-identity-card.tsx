import { AlertCircle, Calendar, Flag, Lock, ShieldCheck, User } from 'lucide-react';

import type { FamilyMember } from '../types/family.types';

interface FamilyMemberIdentityCardProps {
  member: FamilyMember;
}

export function FamilyMemberIdentityCard({ member }: FamilyMemberIdentityCardProps) {
  return (
    <div className="rounded-2xl border border-border bg-white p-6 shadow-xs">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-5">
        <div className="flex items-center gap-3.5">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <ShieldCheck className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-base font-bold tracking-tight text-ink">
              Thông tin định danh y tế
            </h2>
            <p className="mt-0.5 text-xs text-sub">
              Dữ liệu định danh người thân phục vụ tiếp đón và khám chữa bệnh
            </p>
          </div>
        </div>

        <span className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-canvas px-3 py-1 text-xs font-medium text-sub">
          <Lock className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          Chỉ đọc (Read-only)
        </span>
      </div>

      {/* Special Notice or Identity Alert */}
      {member.notice ? (
        <div
          className={`mt-5 flex items-start gap-3 rounded-xl p-4 text-xs leading-relaxed ${
            member.notice.type === 'warning'
              ? 'border border-amber-200/70 bg-amber-50/60 text-amber-800'
              : 'border border-sky-200/70 bg-sky-50/60 text-sky-800'
          }`}
        >
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <p>{member.notice.message}</p>
        </div>
      ) : (
        <div className="mt-5 flex items-start gap-3 rounded-xl border border-amber-200/70 bg-amber-50/60 p-4">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <p className="text-xs leading-relaxed text-amber-800">
            Mã bệnh nhân ({member.patientCode}) cố định trên toàn hệ thống. Vui lòng mang theo giấy
            tờ tùy thân hoặc giấy khai sinh khi đến quầy tiếp đón.
          </p>
        </div>
      )}

      {/* Grid of Fields */}
      <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
        {/* Field 1: Họ và tên */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              Họ và tên
            </span>
            <Lock className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          </div>
          <p className="mt-1.5 text-base font-semibold text-ink">{member.fullName}</p>
        </div>

        {/* Field 2: Ngày sinh & Tuổi */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              Ngày sinh & tuổi
            </span>
            <Calendar className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          </div>
          <div className="mt-1.5 flex items-center gap-2">
            <span className="text-base font-semibold text-ink">{member.birthDate}</span>
            <span className="rounded-md bg-slate-200/80 px-2 py-0.5 text-xs font-semibold text-ink">
              Tuổi: {member.age}
            </span>
          </div>
        </div>

        {/* Field 3: Giới tính & Quan hệ */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              Giới tính & mối quan hệ
            </span>
            <User className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          </div>
          <p className="mt-1.5 text-base font-semibold text-ink">
            {member.gender} • <span className="font-bold">{member.relationship}</span>
          </p>
        </div>

        {/* Field 4: Quốc tịch */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              Quốc tịch
            </span>
            <Flag className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          </div>
          <p className="mt-1.5 text-base font-semibold text-ink">
            {member.nationality ?? 'Việt Nam'}
          </p>
        </div>

        {/* Field 5: Số định danh / CCCD */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              Số CCCD / Mã định danh
            </span>
            <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-sub">
              Bảo mật
            </span>
          </div>
          <p className="mt-1.5 text-base font-semibold text-ink">
            {member.identificationNo ?? 'Chưa cập nhật (Trẻ em / Phụ thuộc)'}
          </p>
        </div>

        {/* Field 6: Ngày cấp & Nơi cấp / Người giám hộ */}
        <div className="rounded-xl border border-border bg-canvas/60 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider text-sub uppercase">
              {member.guardian ? 'Người giám hộ & nơi cấp' : 'Ngày cấp & nơi cấp'}
            </span>
          </div>
          <p className="mt-1.5 text-sm leading-relaxed font-semibold text-ink">
            {member.guardian
              ? `${member.guardian} • ${member.identityIssuePlace ?? 'Cơ quan hộ tịch'}`
              : `${member.identityIssueDate ?? '--'} • ${member.identityIssuePlace ?? 'Cục Cảnh sát QLHC về TTXH'}`}
          </p>
        </div>
      </div>
    </div>
  );
}
