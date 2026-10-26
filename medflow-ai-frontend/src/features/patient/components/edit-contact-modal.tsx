import { useState } from 'react';
import { Modal } from '@/shared/ui/modal';
import type { PatientContact } from '../types/patient-profile.types';

interface EditContactModalProps {
  open: boolean;
  onClose: () => void;
  contact: PatientContact;
  onSave: (updatedContact: PatientContact) => void;
}

export function EditContactModal({ open, onClose, contact, onSave }: EditContactModalProps) {
  const [phone, setPhone] = useState(contact.phone);
  const [email, setEmail] = useState(contact.email);
  const [permanentAddress, setPermanentAddress] = useState(contact.permanentAddress);
  const [emergencyName, setEmergencyName] = useState(contact.emergencyContact.fullName);
  const [emergencyRel, setEmergencyRel] = useState(contact.emergencyContact.relationship);
  const [emergencyPhone, setEmergencyPhone] = useState(contact.emergencyContact.phone);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSave({
      ...contact,
      phone,
      email,
      permanentAddress,
      emergencyContact: {
        fullName: emergencyName,
        relationship: emergencyRel,
        phone: emergencyPhone,
      },
    });
    onClose();
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-xl">
      <form onSubmit={handleSubmit} className="p-6">
        <h3 className="text-base font-bold text-slate-900">Cập nhật thông tin liên hệ & Cư trú</h3>
        <p className="mt-1 text-xs text-slate-500">
          Vui lòng kiểm tra kỹ thông tin liên lạc để nhận lịch hẹn và kết quả xét nghiệm kịp thời.
        </p>

        <div className="mt-5 space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Số điện thoại chính
              </label>
              <input
                type="text"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                required
                className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden focus:ring-2 focus:ring-sky-500/20"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Email nhận kết quả
              </label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden focus:ring-2 focus:ring-sky-500/20"
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-700">
              Địa chỉ thường trú hiện tại
            </label>
            <input
              type="text"
              value={permanentAddress}
              onChange={(e) => setPermanentAddress(e.target.value)}
              required
              className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden focus:ring-2 focus:ring-sky-500/20"
            />
          </div>

          <div className="rounded-xl border border-rose-100 bg-rose-50/30 p-4">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-800">
              Người liên hệ khẩn cấp
            </h4>
            <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div>
                <label className="block text-[11px] font-medium text-slate-600">Họ và tên</label>
                <input
                  type="text"
                  value={emergencyName}
                  onChange={(e) => setEmergencyName(e.target.value)}
                  required
                  className="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-900 focus:border-sky-500 focus:outline-hidden"
                />
              </div>

              <div>
                <label className="block text-[11px] font-medium text-slate-600">Mối quan hệ</label>
                <input
                  type="text"
                  value={emergencyRel}
                  onChange={(e) => setEmergencyRel(e.target.value)}
                  required
                  className="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-900 focus:border-sky-500 focus:outline-hidden"
                />
              </div>

              <div>
                <label className="block text-[11px] font-medium text-slate-600">
                  Số điện thoại
                </label>
                <input
                  type="text"
                  value={emergencyPhone}
                  onChange={(e) => setEmergencyPhone(e.target.value)}
                  required
                  className="mt-1 w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-900 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>
          </div>
        </div>

        <div className="mt-6 flex items-center justify-end gap-3 border-t border-slate-100 pt-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-slate-200 px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50"
          >
            Hủy
          </button>
          <button
            type="submit"
            className="rounded-xl bg-sky-600 px-4 py-2 text-xs font-bold text-white shadow-sm hover:bg-sky-700"
          >
            Lưu thay đổi
          </button>
        </div>
      </form>
    </Modal>
  );
}
