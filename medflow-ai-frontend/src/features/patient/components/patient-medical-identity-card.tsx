import { AlertCircle, Calendar, Flag, Lock, ShieldCheck } from 'lucide-react';
import type { PatientIdentity } from '../types/patient-profile.types';

interface PatientMedicalIdentityCardProps {
  identity: PatientIdentity;
}

export function PatientMedicalIdentityCard({ identity }: PatientMedicalIdentityCardProps) {
  return (
    <div className="rounded-2xl border border-slate-200/90 bg-white p-6 shadow-xs">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-5">
        <div className="flex items-center gap-3.5">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-sky-600">
            <ShieldCheck className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-900">
              Thông tin định danh y tế
            </h2>
            <p className="mt-0.5 text-xs text-slate-400">
              Dữ liệu nguồn bảo đảm pháp lý hồ sơ bệnh án
            </p>
          </div>
        </div>

        <span className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200/80 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-600">
          <Lock className="h-3 w-3 text-slate-400" />
          Chỉ đọc (Read-only)
        </span>
      </div>

      {/* Info Alert Box */}
      <div className="mt-5 flex items-start gap-3 rounded-xl border border-amber-200/70 bg-amber-50/60 p-4">
        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
        <p className="text-xs leading-relaxed text-amber-800">
          Thông tin định danh cố định để đồng bộ hồ sơ bệnh án điện tử (PID). Nếu sai lệch, vui lòng
          liên hệ quầy tiếp đón bệnh viện để đối soát CCCD.
        </p>
      </div>

      {/* Grid of Fields */}
      <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
        {/* Field 1: Họ và tên */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Họ và tên
            </span>
            <Lock className="h-3.5 w-3.5 text-slate-400" />
          </div>
          <p className="mt-1.5 text-sm font-bold tracking-wide text-slate-900">
            {identity.fullName}
          </p>
        </div>

        {/* Field 2: Ngày sinh & Tuổi */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Ngày sinh & tuổi
            </span>
            <Calendar className="h-3.5 w-3.5 text-slate-400" />
          </div>
          <div className="mt-1.5 flex items-center gap-2">
            <span className="text-sm font-bold text-slate-900">{identity.dateOfBirth}</span>
            <span className="rounded-md bg-slate-200/80 px-2 py-0.5 text-xs font-semibold text-slate-700">
              Tuổi: {identity.age}
            </span>
          </div>
        </div>

        {/* Field 3: Giới tính */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Giới tính
            </span>
            <span className="text-sm font-bold text-slate-400">♂</span>
          </div>
          <p className="mt-1.5 text-sm font-bold text-slate-900">{identity.gender}</p>
        </div>

        {/* Field 4: Quốc tịch */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Quốc tịch
            </span>
            <Flag className="h-3.5 w-3.5 text-slate-400" />
          </div>
          <p className="mt-1.5 text-sm font-bold text-slate-900">{identity.nationality}</p>
        </div>

        {/* Field 5: Số CCCD / Mã định danh */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Số CCCD / Mã định danh
            </span>
            <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">
              Bảo mật
            </span>
          </div>
          <p className="mt-1.5 text-sm font-bold text-slate-900">{identity.identityNumber}</p>
        </div>

        {/* Field 6: Ngày cấp & Nơi cấp */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-3.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
              Ngày cấp & nơi cấp
            </span>
          </div>
          <p className="mt-1.5 text-xs font-semibold leading-relaxed text-slate-900">
            {identity.identityIssueDate} • {identity.identityIssuePlace}
          </p>
        </div>
      </div>
    </div>
  );
}
