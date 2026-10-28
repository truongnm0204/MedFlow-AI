import { Check, Edit2, Mail, MapPin, Phone, PhoneCall, UserCheck } from 'lucide-react';
import type { PatientContact } from '../types/patient-profile.types';

interface PatientContactCardProps {
  contact: PatientContact;
  onEditContact?: () => void;
}

export function PatientContactCard({ contact, onEditContact }: PatientContactCardProps) {
  const { emergencyContact } = contact;

  return (
    <div className="mt-6 rounded-2xl border border-slate-200/90 bg-white p-6 shadow-xs">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-5">
        <div className="flex items-center gap-3.5">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-sky-600">
            <UserCheck className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-900">
              Thông tin liên hệ & cư trú
            </h2>
            <p className="mt-0.5 text-xs text-slate-400">
              Kênh nhận thông báo lịch hẹn, hóa đơn & kết quả xét nghiệm
            </p>
          </div>
        </div>

        <button
          type="button"
          onClick={onEditContact}
          className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50 active:scale-[0.99]"
        >
          <Edit2 className="h-3.5 w-3.5 text-slate-500" />
          Cập nhật
        </button>
      </div>

      {/* Row 1: Phone & Email */}
      <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
        {/* Phone */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
              <Phone className="h-3.5 w-3.5" />
              Số điện thoại chính
            </span>
            {contact.isPhoneVerified && (
              <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200/70 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                <Check className="h-3 w-3 stroke-[2.5]" />
                Đã xác thực SMS
              </span>
            )}
          </div>
          <p className="mt-2 font-mono text-base font-bold tracking-wide text-slate-900">
            {contact.phone}
          </p>
        </div>

        {/* Email */}
        <div className="rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
              <Mail className="h-3.5 w-3.5" />
              Email nhận kết quả
            </span>
            {contact.isEmailVerified && (
              <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200/70 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                <Check className="h-3 w-3 stroke-[2.5]" />
                Đã xác thực Email
              </span>
            )}
          </div>
          <p className="mt-2 text-sm font-bold text-slate-900">{contact.email}</p>
        </div>
      </div>

      {/* Row 2: Address */}
      <div className="mt-4 rounded-xl border border-slate-200/80 bg-slate-50/40 p-4">
        <span className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
          <MapPin className="h-3.5 w-3.5 text-slate-400" />
          Địa chỉ thường trú hiện tại
        </span>
        <p className="mt-2 text-sm font-semibold leading-relaxed text-slate-900">
          {contact.permanentAddress}
        </p>
      </div>

      {/* Subsection: Emergency Contact */}
      <div className="mt-5 rounded-xl border border-rose-100 bg-rose-50/30 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <PhoneCall className="h-4 w-4 text-rose-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-800">
              Người liên hệ khẩn cấp (Emergency Contact)
            </h3>
          </div>
          <span className="rounded-md border border-sky-200/70 bg-sky-50 px-2 py-0.5 text-[10px] font-semibold text-sky-700">
            Bắt buộc theo y tế số
          </span>
        </div>

        <div className="mt-3 grid grid-cols-1 gap-3 rounded-xl border border-slate-200/80 bg-white p-3.5 sm:grid-cols-3">
          <div>
            <span className="block text-[11px] font-medium text-slate-400">Họ và tên</span>
            <span className="mt-1 block text-xs font-bold text-slate-900">
              {emergencyContact.fullName}
            </span>
          </div>

          <div>
            <span className="block text-[11px] font-medium text-slate-400">Mối quan hệ</span>
            <span className="mt-1 inline-flex rounded-md border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-xs font-semibold text-sky-700">
              {emergencyContact.relationship}
            </span>
          </div>

          <div>
            <span className="block text-[11px] font-medium text-slate-400">
              Số điện thoại khẩn cấp
            </span>
            <span className="mt-1 block font-mono text-xs font-bold text-slate-900">
              {emergencyContact.phone}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
