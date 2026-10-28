import { useState } from 'react';
import { Asterisk, Check, FileEdit, Lock, Mail, MapPin, Phone, Users } from 'lucide-react';
import { Modal } from '@/shared/ui/modal';
import type { PatientContact, PatientIdentity } from '../types/patient-profile.types';

interface EditContactModalProps {
  open: boolean;
  onClose: () => void;
  identity: PatientIdentity;
  contact: PatientContact;
  onSave: (updatedContact: PatientContact) => void;
}

export function EditContactModal({
  open,
  onClose,
  identity,
  contact,
  onSave,
}: EditContactModalProps) {
  const [phone, setPhone] = useState(contact.phone);
  const [email, setEmail] = useState(contact.email);
  const [permanentAddress, setPermanentAddress] = useState(contact.permanentAddress);
  const [emergencyNameAndRel, setEmergencyNameAndRel] = useState(
    `${contact.emergencyContact.fullName} - ${contact.emergencyContact.relationship}`,
  );
  const [emergencyPhone, setEmergencyPhone] = useState(contact.emergencyContact.phone);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    // Parse Họ tên & Mối quan hệ
    const parts = emergencyNameAndRel.split('-').map((s) => s.trim());
    const fullName = parts[0] || contact.emergencyContact.fullName;
    const relationship = parts[1] || contact.emergencyContact.relationship;

    onSave({
      ...contact,
      phone,
      email,
      permanentAddress,
      emergencyContact: {
        fullName,
        relationship,
        phone: emergencyPhone,
      },
    });
    onClose();
  };

  // Trích xuất mã CCCD sạch (không kèm chú thích mở ngoặc)
  const cccdCode = identity.identityNumber.split(' ')[0] || identity.identityNumber;

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form onSubmit={handleSubmit} className="p-7">
        {/* Header */}
        <div className="flex items-center gap-3.5 pr-8">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-sky-50 text-sky-600">
            <FileEdit className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-base font-bold text-slate-900 sm:text-lg">
              Cập nhật Thông tin Liên hệ Cá nhân
            </h3>
            <p className="mt-0.5 text-xs text-slate-400">
              Đồng bộ hồ sơ khám bệnh ngoại trú trực tuyến
            </p>
          </div>
        </div>

        {/* Legal Alert Note */}
        <div className="mt-5 flex items-start gap-3 rounded-2xl border border-sky-200/80 bg-sky-50/60 p-4">
          <div className="mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-sky-600 text-[10px] font-bold text-white">
            i
          </div>
          <p className="text-xs leading-relaxed text-slate-700">
            <strong className="font-semibold text-sky-800">Lưu ý:</strong> Để đảm bảo an toàn pháp
            lý của Hồ sơ bệnh án điện tử, Họ tên và Số CCCD được khóa cố định. Bệnh nhân có thể tự
            cập nhật Số điện thoại, Email và Địa chỉ nơi ở.
          </p>
        </div>

        {/* Section 1: Legal Identity (Read-only) */}
        <div className="mt-6">
          <h4 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
            Thông tin định danh pháp lý (Không thể sửa)
          </h4>
          <div className="mt-2.5 grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <span className="block text-xs font-medium text-slate-600">Họ và tên bệnh nhân</span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-slate-200/90 bg-slate-50/70 px-3.5 py-2.5">
                <span className="text-xs font-bold uppercase tracking-wide text-slate-800">
                  {identity.fullName}
                </span>
                <Lock className="h-3.5 w-3.5 text-slate-400" />
              </div>
            </div>

            <div>
              <span className="block text-xs font-medium text-slate-600">Số thẻ CCCD</span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-slate-200/90 bg-slate-50/70 px-3.5 py-2.5">
                <span className="font-mono text-xs font-bold tracking-wide text-slate-800">
                  {cccdCode}
                </span>
                <Lock className="h-3.5 w-3.5 text-slate-400" />
              </div>
            </div>
          </div>
        </div>

        {/* Section 2: Contact Information */}
        <div className="mt-6">
          <h4 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
            Thông tin tiếp nhận kết quả & liên hệ
          </h4>

          <div className="mt-3 space-y-4">
            {/* Phone */}
            <div>
              <div className="flex items-center justify-between">
                <label className="block text-xs font-semibold text-slate-700">
                  Số điện thoại liên hệ chính <span className="text-rose-500">*</span>
                </label>
                <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200/80 bg-emerald-50 px-2.5 py-0.5 text-[11px] font-semibold text-emerald-700">
                  <Check className="h-3 w-3 stroke-[2.5]" />
                  Đã xác thực OTP
                </span>
              </div>
              <div className="relative mt-1.5">
                <Phone className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  type="text"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  required
                  className="w-full rounded-xl border border-sky-400 bg-white py-2.5 pl-10 pr-4 font-mono text-xs font-semibold text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                />
              </div>
            </div>

            {/* Email */}
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Email nhận kết quả xét nghiệm / đơn thuốc <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5">
                <Mail className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                  className="w-full rounded-xl border border-slate-300 bg-white py-2.5 pl-10 pr-4 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                />
              </div>
              <p className="mt-1.5 text-[11px] text-slate-400">
                File kết quả xét nghiệm định dạng PDF kèm chữ ký số bảo mật sẽ được gửi về hộp thư
                này.
              </p>
            </div>

            {/* Address */}
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Địa chỉ thường trú / nơi ở hiện tại <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5">
                <MapPin className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  type="text"
                  value={permanentAddress}
                  onChange={(e) => setPermanentAddress(e.target.value)}
                  required
                  className="w-full rounded-xl border border-slate-300 bg-white py-2.5 pl-10 pr-4 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                />
              </div>
            </div>

            {/* Emergency Contact */}
            <div className="pt-2">
              <h5 className="text-xs font-bold text-slate-800">
                Thông tin người liên hệ khẩn cấp (Emergency Contact)
              </h5>
              <div className="mt-2.5 grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                  <label className="block text-[11px] font-medium text-slate-500">
                    Họ tên & mối quan hệ
                  </label>
                  <div className="relative mt-1">
                    <Users className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                    <input
                      type="text"
                      value={emergencyNameAndRel}
                      onChange={(e) => setEmergencyNameAndRel(e.target.value)}
                      required
                      placeholder="Họ tên - Mối quan hệ"
                      className="w-full rounded-xl border border-slate-300 bg-white py-2 pl-9 pr-3 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-[11px] font-medium text-slate-500">
                    Số điện thoại khẩn cấp
                  </label>
                  <div className="relative mt-1">
                    <Asterisk className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                    <input
                      type="text"
                      value={emergencyPhone}
                      onChange={(e) => setEmergencyPhone(e.target.value)}
                      required
                      className="w-full rounded-xl border border-slate-300 bg-white py-2 pl-9 pr-3 font-mono text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Footer Actions */}
        <div className="mt-8 flex items-center justify-end gap-3 border-t border-slate-100 pt-5">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50"
          >
            Hủy bỏ
          </button>
          <button
            type="submit"
            className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-6 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
          >
            <Check className="h-4 w-4 stroke-[2.5]" />
            Lưu thay đổi
          </button>
        </div>
      </form>
    </Modal>
  );
}
