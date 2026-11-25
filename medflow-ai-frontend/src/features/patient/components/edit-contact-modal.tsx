import { useState } from 'react';
import {
  Calendar,
  Check,
  Droplets,
  FileEdit,
  HeartHandshake,
  Info,
  Lock,
  Mail,
  MapPin,
  Phone,
  User,
  Users,
} from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Modal } from '@/shared/ui/modal';

import type {
  GenderCode,
  PatientContact,
  PatientIdentity,
  PatientVitals,
  UpdateMyPatientProfileInput,
} from '../types/patient-profile.types';

const UNSET_LABEL = 'Chưa cập nhật';
const BLOOD_TYPE_OPTIONS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'] as const;
const VN_PHONE_REGEX = /^0[35789]\d{8}$/;

interface EditContactModalProps {
  open: boolean;
  onClose: () => void;
  identity: PatientIdentity;
  vitals: PatientVitals;
  contact: PatientContact;
  isSubmitting?: boolean;
  onSave: (payload: UpdateMyPatientProfileInput) => Promise<void>;
}

function cleanDisplayValue(value: string): string {
  return value === UNSET_LABEL ? '' : value;
}

export function EditContactModal({
  open,
  onClose,
  identity,
  vitals,
  contact,
  isSubmitting = false,
  onSave,
}: EditContactModalProps) {
  const [phone, setPhone] = useState(() => cleanDisplayValue(contact.phone));
  const [permanentAddress, setPermanentAddress] = useState(() =>
    cleanDisplayValue(contact.permanentAddress),
  );
  const [bloodType, setBloodType] = useState(() => cleanDisplayValue(vitals.bloodType));
  const [dateOfBirth, setDateOfBirth] = useState(() => identity.rawDateOfBirthIso ?? '');
  const [gender, setGender] = useState<GenderCode | ''>(() => identity.rawGenderCode ?? '');
  const [emergencyName, setEmergencyName] = useState(() =>
    cleanDisplayValue(contact.emergencyContact.fullName),
  );
  const [emergencyRelationship, setEmergencyRelationship] = useState(() =>
    cleanDisplayValue(contact.emergencyContact.relationship),
  );
  const [emergencyPhone, setEmergencyPhone] = useState(() =>
    cleanDisplayValue(contact.emergencyContact.phone),
  );
  const [formError, setFormError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const normalizedPhone = phone.trim();
    if (!VN_PHONE_REGEX.test(normalizedPhone)) {
      setFormError('Số điện thoại liên hệ chính không hợp lệ (gồm 10 chữ số di động Việt Nam).');
      return;
    }

    const normalizedAddress = permanentAddress.trim();
    if (normalizedAddress.length > 500) {
      setFormError('Địa chỉ thường trú tối đa 500 ký tự.');
      return;
    }

    const trimmedEcName = emergencyName.trim();
    const trimmedEcRel = emergencyRelationship.trim();
    const trimmedEcPhone = emergencyPhone.trim();
    const hasAnyEmergencyField =
      trimmedEcName.length > 0 || trimmedEcRel.length > 0 || trimmedEcPhone.length > 0;

    if (hasAnyEmergencyField) {
      if (trimmedEcName.length < 2 || trimmedEcName.length > 200) {
        setFormError('Họ tên người liên hệ khẩn cấp phải từ 2 đến 200 ký tự.');
        return;
      }
      if (!trimmedEcRel || trimmedEcRel.length > 60) {
        setFormError('Vui lòng nhập mối quan hệ người liên hệ khẩn cấp (tối đa 60 ký tự).');
        return;
      }
      if (!VN_PHONE_REGEX.test(trimmedEcPhone)) {
        setFormError('Số điện thoại khẩn cấp không hợp lệ (gồm 10 chữ số di động Việt Nam).');
        return;
      }
    }

    const payload: UpdateMyPatientProfileInput = {
      phone: normalizedPhone,
      address: normalizedAddress || null,
      bloodType: bloodType.trim() || null,
      emergencyContact: hasAnyEmergencyField
        ? {
            fullName: trimmedEcName,
            relationship: trimmedEcRel,
            phone: trimmedEcPhone,
          }
        : null,
    };

    if (!identity.isIdentityVerified) {
      if (dateOfBirth.trim()) {
        payload.dateOfBirth = dateOfBirth.trim();
      }
      if (gender) {
        payload.gender = gender;
      }
    }

    try {
      await onSave(payload);
      onClose();
    } catch (err) {
      setFormError(getErrorMessage(err, 'Không thể lưu thay đổi hồ sơ. Vui lòng thử lại.'));
    }
  };

  const cccdCode = identity.identityNumber.split(' ')[0] || identity.identityNumber;

  return (
    <Modal open={open} onClose={isSubmitting ? () => undefined : onClose} maxWidth="max-w-2xl">
      <form onSubmit={(e) => void handleSubmit(e)} className="p-6 sm:p-7" noValidate>
        <div className="flex items-center gap-3.5 pr-8">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-sky-50 text-primary">
            <FileEdit className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h3 className="text-base font-bold text-ink sm:text-lg">
              Cập nhật Thông tin Liên hệ Cá nhân
            </h3>
            <p className="mt-0.5 text-xs text-sub">
              Đồng bộ trực tiếp vào hồ sơ khám bệnh ngoại trú của bạn
            </p>
          </div>
        </div>

        <div className="mt-5 flex items-start gap-3 rounded-2xl border border-sky-200/80 bg-sky-50/60 p-4">
          <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <p className="text-xs leading-relaxed text-ink">
            <strong className="font-semibold text-navy">Lưu ý pháp lý:</strong> Họ tên, Số CCCD và
            Email đăng nhập được khóa cố định để bảo vệ hồ sơ bệnh án điện tử.
            {identity.isIdentityVerified
              ? ' Ngày sinh và Giới tính đã khóa theo CCCD xác thực.'
              : ' Bạn có thể bổ sung Ngày sinh và Giới tính trước khi đối chiếu CCCD tại quầy.'}
          </p>
        </div>

        {formError && (
          <Alert tone="error" className="mt-4">
            {formError}
          </Alert>
        )}

        <div className="mt-5">
          <h4 className="text-[11px] font-bold tracking-wider text-sub uppercase">
            Thông tin định danh pháp lý (Chỉ đọc)
          </h4>
          <div className="mt-2.5 grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div>
              <span className="block text-xs font-medium text-sub">Họ và tên bệnh nhân</span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-border bg-canvas px-3 py-2.5">
                <span className="truncate text-xs font-bold tracking-wide text-ink uppercase">
                  {identity.fullName}
                </span>
                <Lock className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
              </div>
            </div>

            <div>
              <span className="block text-xs font-medium text-sub">Số thẻ CCCD</span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-border bg-canvas px-3 py-2.5">
                <span className="truncate font-mono text-xs font-bold tracking-wide text-ink">
                  {cccdCode}
                </span>
                <Lock className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
              </div>
            </div>

            <div>
              <span className="block text-xs font-medium text-sub">Email đăng nhập</span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-border bg-canvas px-3 py-2.5">
                <span className="truncate text-xs font-semibold text-ink">
                  <Mail className="mr-1.5 inline h-3.5 w-3.5 text-sub" aria-hidden="true" />
                  {contact.email}
                </span>
                <Lock className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
              </div>
            </div>
          </div>
        </div>

        {!identity.isIdentityVerified && (
          <div className="mt-5 grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="profile-dob" className="block text-xs font-semibold text-ink">
                Ngày sinh (chưa khóa CCCD)
              </label>
              <div className="relative mt-1.5">
                <Calendar
                  className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-sub"
                  aria-hidden="true"
                />
                <input
                  id="profile-dob"
                  type="date"
                  value={dateOfBirth}
                  onChange={(e) => setDateOfBirth(e.target.value)}
                  disabled={isSubmitting}
                  className="w-full rounded-xl border border-border bg-white py-2.5 pr-4 pl-10 text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                />
              </div>
            </div>

            <div>
              <label htmlFor="profile-gender" className="block text-xs font-semibold text-ink">
                Giới tính (chưa khóa CCCD)
              </label>
              <div className="relative mt-1.5">
                <User
                  className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-sub"
                  aria-hidden="true"
                />
                <select
                  id="profile-gender"
                  value={gender}
                  onChange={(e) => setGender(e.target.value as GenderCode | '')}
                  disabled={isSubmitting}
                  className="w-full rounded-xl border border-border bg-white py-2.5 pr-4 pl-10 text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                >
                  <option value="">-- Giữ nguyên --</option>
                  <option value="MALE">Nam</option>
                  <option value="FEMALE">Nữ</option>
                  <option value="OTHER">Khác</option>
                </select>
              </div>
            </div>
          </div>
        )}

        <div className="mt-5 space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="profile-phone" className="block text-xs font-semibold text-ink">
                Số điện thoại liên hệ chính <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5">
                <Phone
                  className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-sub"
                  aria-hidden="true"
                />
                <input
                  id="profile-phone"
                  type="tel"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  disabled={isSubmitting}
                  placeholder="0912345678"
                  required
                  className="w-full rounded-xl border border-border bg-white py-2.5 pr-4 pl-10 font-mono text-xs font-semibold text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                />
              </div>
            </div>

            <div>
              <label htmlFor="profile-blood-type" className="block text-xs font-semibold text-ink">
                Nhóm máu (tự khai báo)
              </label>
              <div className="relative mt-1.5">
                <Droplets
                  className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-rose-500"
                  aria-hidden="true"
                />
                <select
                  id="profile-blood-type"
                  value={bloodType}
                  onChange={(e) => setBloodType(e.target.value)}
                  disabled={isSubmitting}
                  className="w-full rounded-xl border border-border bg-white py-2.5 pr-4 pl-10 text-xs font-semibold text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                >
                  <option value="">Chưa cập nhật</option>
                  {BLOOD_TYPE_OPTIONS.map((bt) => (
                    <option key={bt} value={bt}>
                      Nhóm máu {bt} (tự khai báo)
                    </option>
                  ))}
                </select>
              </div>
              <p className="mt-1 text-[11px] text-sub">
                Thông tin tự khai báo chỉ dùng tham khảo; chỉ định lâm sàng luôn xét nghiệm định
                nhóm máu tại viện.
              </p>
            </div>
          </div>

          <div>
            <label htmlFor="profile-address" className="block text-xs font-semibold text-ink">
              Địa chỉ thường trú / nơi ở hiện tại
            </label>
            <div className="relative mt-1.5">
              <MapPin
                className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-sub"
                aria-hidden="true"
              />
              <input
                id="profile-address"
                type="text"
                value={permanentAddress}
                onChange={(e) => setPermanentAddress(e.target.value)}
                disabled={isSubmitting}
                placeholder="Số nhà, đường, phường/xã, quận/huyện, tỉnh/thành phố"
                className="w-full rounded-xl border border-border bg-white py-2.5 pr-4 pl-10 text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
              />
            </div>
          </div>

          <div className="rounded-xl border border-border bg-canvas/70 p-4">
            <h5 className="text-xs font-bold text-ink">Người liên hệ khẩn cấp (Tùy chọn)</h5>
            <p className="mt-0.5 text-[11px] text-sub">
              Chỉ dùng để liên hệ trong tình huống y tế khẩn cấp; không cấp quyền xem bệnh án hay
              quản lý hồ sơ gia đình.
            </p>
            <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div>
                <label htmlFor="emergency-name" className="block text-[11px] font-medium text-sub">
                  Họ và tên người liên hệ
                </label>
                <div className="relative mt-1">
                  <Users
                    className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
                    aria-hidden="true"
                  />
                  <input
                    id="emergency-name"
                    type="text"
                    value={emergencyName}
                    onChange={(e) => setEmergencyName(e.target.value)}
                    disabled={isSubmitting}
                    placeholder="Nguyễn Thị B"
                    className="w-full rounded-xl border border-border bg-white py-2 pr-3 pl-9 text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                  />
                </div>
              </div>

              <div>
                <label
                  htmlFor="emergency-relationship"
                  className="block text-[11px] font-medium text-sub"
                >
                  Mối quan hệ
                </label>
                <div className="relative mt-1">
                  <HeartHandshake
                    className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
                    aria-hidden="true"
                  />
                  <input
                    id="emergency-relationship"
                    type="text"
                    value={emergencyRelationship}
                    onChange={(e) => setEmergencyRelationship(e.target.value)}
                    disabled={isSubmitting}
                    placeholder="Vợ / Chồng / Cha / Mẹ"
                    className="w-full rounded-xl border border-border bg-white py-2 pr-3 pl-9 text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                  />
                </div>
              </div>

              <div>
                <label htmlFor="emergency-phone" className="block text-[11px] font-medium text-sub">
                  Số điện thoại khẩn cấp
                </label>
                <div className="relative mt-1">
                  <Phone
                    className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
                    aria-hidden="true"
                  />
                  <input
                    id="emergency-phone"
                    type="tel"
                    value={emergencyPhone}
                    onChange={(e) => setEmergencyPhone(e.target.value)}
                    disabled={isSubmitting}
                    placeholder="0987654321"
                    className="w-full rounded-xl border border-border bg-white py-2 pr-3 pl-9 font-mono text-xs font-medium text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-hidden disabled:opacity-60"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>

        <div className="mt-6 flex items-center justify-end gap-3 border-t border-border pt-4">
          <button
            type="button"
            onClick={onClose}
            disabled={isSubmitting}
            className="rounded-xl border border-border bg-white px-5 py-2.5 text-xs font-semibold text-ink transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
          >
            Hủy bỏ
          </button>
          <button
            type="submit"
            disabled={isSubmitting}
            className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-6 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
          >
            <Check className="h-4 w-4" aria-hidden="true" />
            <span>{isSubmitting ? 'Đang lưu...' : 'Lưu thay đổi'}</span>
          </button>
        </div>
      </form>
    </Modal>
  );
}
