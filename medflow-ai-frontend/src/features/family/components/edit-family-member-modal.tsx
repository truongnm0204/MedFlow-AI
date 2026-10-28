import { useState } from 'react';
import {
  AlertCircle,
  Asterisk,
  Check,
  FileEdit,
  Info,
  Lock,
  Mail,
  MapPin,
  Phone,
  Users,
} from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import { useUpdateFamilyMember } from '../hooks/use-update-family-member';
import {
  updateFamilyMemberSchema,
  type UpdateFamilyMemberInput,
} from '../schemas/family-member.schema';
import type { FamilyMember } from '../types/family.types';

interface EditFamilyMemberModalProps {
  open: boolean;
  onClose: () => void;
  member: FamilyMember;
  onSave?: (updatedMember: FamilyMember) => void;
}

interface EditFamilyMemberFormProps {
  member: FamilyMember;
  onClose: () => void;
  onSave?: (updatedMember: FamilyMember) => void;
}

function EditFamilyMemberForm({ member, onClose, onSave }: EditFamilyMemberFormProps) {
  const updateMutation = useUpdateFamilyMember();

  const [phone, setPhone] = useState(member.phone ?? member.emergencyPhone ?? '');
  const [email, setEmail] = useState(member.email ?? '');
  const [address, setAddress] = useState(member.address ?? '');

  const defaultRel = member.emergencyContactRelation ?? 'Chủ tài khoản chính';
  const defaultName = member.emergencyContactName ?? member.guardian ?? 'Nguyễn Văn An';
  const [emergencyNameAndRel, setEmergencyNameAndRel] = useState(
    defaultName.includes('-') ? defaultName : `${defaultName} - ${defaultRel}`,
  );
  const [emergencyPhone, setEmergencyPhone] = useState(
    member.emergencyPhone ?? member.phone ?? '0912 345 678',
  );
  const [errors, setErrors] = useState<Record<string, string>>({});

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrors({});

    // Parse Họ tên & Mối quan hệ
    const parts = emergencyNameAndRel.split('-').map((s) => s.trim());
    const emergencyContactName = parts[0] || member.emergencyContactName || 'Nguyễn Văn An';
    const emergencyContactRelation =
      parts[1] || member.emergencyContactRelation || 'Chủ tài khoản chính';

    const inputData: UpdateFamilyMemberInput = {
      phone: phone.trim() || undefined,
      email: email.trim() || undefined,
      address: address.trim(),
      emergencyContactName,
      emergencyContactRelation,
      emergencyPhone: emergencyPhone.trim() || undefined,
    };

    const result = updateFamilyMemberSchema.safeParse(inputData);
    if (!result.success) {
      const fieldErrors: Record<string, string> = {};
      for (const issue of result.error.issues) {
        const fieldName = issue.path[0]?.toString() ?? 'form';
        if (!fieldErrors[fieldName]) {
          fieldErrors[fieldName] = issue.message;
        }
      }
      setErrors(fieldErrors);
      return;
    }

    try {
      const updated = await updateMutation.mutateAsync({
        memberId: member.id,
        payload: result.data,
      });
      onSave?.(updated);
      onClose();
    } catch (err: unknown) {
      setErrors({
        form: err instanceof Error ? err.message : 'Không thể cập nhật hồ sơ, vui lòng thử lại.',
      });
    }
  };

  const isSubmitting = updateMutation.isPending;
  const displayCode = member.identificationNo ?? member.patientCode;

  return (
    <div className="relative p-6 sm:p-7">
      {/* Header */}
      <div className="flex items-center gap-3.5 pr-8">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-sky-50 text-sky-600">
          <FileEdit className="h-5 w-5" />
        </div>
        <div>
          <h3 className="text-base font-bold text-slate-900 sm:text-lg">
            Cập nhật Thông tin Hồ sơ Người thân
          </h3>
          <p className="mt-0.5 text-xs text-slate-500">
            Đồng bộ hồ sơ khám bệnh ngoại trú trực tuyến
          </p>
        </div>
      </div>

      {/* Legal Alert Note */}
      <div className="mt-5 flex items-start gap-3 rounded-2xl border border-sky-200/80 bg-sky-50/70 p-4">
        <div className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sky-600 text-white">
          <Info className="h-3.5 w-3.5" />
        </div>
        <p className="text-xs leading-relaxed text-slate-700">
          <strong className="font-semibold text-sky-900">Lưu ý:</strong> Để đảm bảo an toàn pháp lý
          của Hồ sơ bệnh án điện tử, Họ tên và Số CCCD/Mã bệnh nhân được khóa cố định. Bệnh nhân có
          thể tự cập nhật Số điện thoại, Email, Địa chỉ nơi ở và Người liên hệ khẩn cấp.
        </p>
      </div>

      {/* Global Error Banner if any */}
      {errors.form && (
        <div className="mt-4 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs text-rose-700">
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span>{errors.form}</span>
        </div>
      )}

      <form onSubmit={handleSubmit} className="mt-6">
        {/* Section 1: Legal Identity (Read-only) */}
        <div>
          <h4 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
            Thông tin định danh pháp lý (Không thể sửa)
          </h4>
          <div className="mt-2.5 grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <span className="block text-xs font-semibold text-slate-600">
                Họ và tên người thân
              </span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-slate-200/90 bg-slate-50/80 px-3.5 py-2.5">
                <span className="text-xs font-bold uppercase tracking-wide text-slate-800">
                  {member.fullName}
                </span>
                <Lock className="h-3.5 w-3.5 text-slate-400" />
              </div>
            </div>

            <div>
              <span className="block text-xs font-semibold text-slate-600">
                Số thẻ CCCD / Mã bệnh nhân
              </span>
              <div className="mt-1.5 flex items-center justify-between rounded-xl border border-slate-200/90 bg-slate-50/80 px-3.5 py-2.5">
                <span className="font-mono text-xs font-bold tracking-wide text-slate-800">
                  {displayCode}
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
                <label htmlFor="family-edit-phone" className="text-xs font-semibold text-slate-700">
                  Số điện thoại liên hệ chính <span className="text-rose-500">*</span>
                </label>
                <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                  <Check className="h-3 w-3 stroke-[2.5]" />
                  Nhận SMS lịch hẹn
                </span>
              </div>
              <div className="relative mt-1.5">
                <Phone className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  id="family-edit-phone"
                  type="text"
                  value={phone}
                  onChange={(e) => {
                    setPhone(e.target.value);
                    if (errors.phone) setErrors((prev) => ({ ...prev, phone: '' }));
                  }}
                  placeholder="0912 345 678"
                  className={`w-full rounded-xl border bg-white py-2.5 pr-4 pl-10 font-mono text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15 ${
                    errors.phone ? 'border-rose-400' : 'border-slate-300'
                  }`}
                />
              </div>
              {errors.phone && <p className="mt-1 text-xs text-rose-600">{errors.phone}</p>}
            </div>

            {/* Email */}
            <div>
              <label
                htmlFor="family-edit-email"
                className="block text-xs font-semibold text-slate-700"
              >
                Email nhận kết quả xét nghiệm / đơn thuốc <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5">
                <Mail className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  id="family-edit-email"
                  type="email"
                  value={email}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    if (errors.email) setErrors((prev) => ({ ...prev, email: '' }));
                  }}
                  placeholder="example@patient.medflow.vn"
                  className={`w-full rounded-xl border bg-white py-2.5 pr-4 pl-10 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15 ${
                    errors.email ? 'border-rose-400' : 'border-slate-300'
                  }`}
                />
              </div>
              {errors.email ? (
                <p className="mt-1 text-xs text-rose-600">{errors.email}</p>
              ) : (
                <p className="mt-1.5 text-[11px] text-slate-500">
                  File kết quả xét nghiệm định dạng PDF kèm chữ ký số bảo mật sẽ được gửi về hộp thư
                  này.
                </p>
              )}
            </div>

            {/* Address */}
            <div>
              <label
                htmlFor="family-edit-address"
                className="block text-xs font-semibold text-slate-700"
              >
                Địa chỉ thường trú / nơi ở hiện tại <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5">
                <MapPin className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  id="family-edit-address"
                  type="text"
                  value={address}
                  onChange={(e) => {
                    setAddress(e.target.value);
                    if (errors.address) setErrors((prev) => ({ ...prev, address: '' }));
                  }}
                  placeholder="Số nhà, tên đường, phường/xã, quận/huyện, tỉnh/thành"
                  className={`w-full rounded-xl border bg-white py-2.5 pr-4 pl-10 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15 ${
                    errors.address ? 'border-rose-400' : 'border-slate-300'
                  }`}
                />
              </div>
              {errors.address && <p className="mt-1 text-xs text-rose-600">{errors.address}</p>}
            </div>

            {/* Emergency Contact */}
            <div className="pt-2">
              <h5 className="text-xs font-bold text-slate-800">
                Thông tin người liên hệ khẩn cấp (Emergency Contact)
              </h5>
              <div className="mt-2.5 grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                  <label
                    htmlFor="family-edit-emergency-name"
                    className="block text-[11px] font-medium text-slate-500"
                  >
                    Họ tên & mối quan hệ
                  </label>
                  <div className="relative mt-1">
                    <Users className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-400" />
                    <input
                      id="family-edit-emergency-name"
                      type="text"
                      value={emergencyNameAndRel}
                      onChange={(e) => setEmergencyNameAndRel(e.target.value)}
                      placeholder="Họ tên - Mối quan hệ"
                      className="w-full rounded-xl border border-slate-300 bg-white py-2 pr-3 pl-9 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15"
                    />
                  </div>
                </div>

                <div>
                  <label
                    htmlFor="family-edit-emergency-phone"
                    className="block text-[11px] font-medium text-slate-500"
                  >
                    Số điện thoại khẩn cấp
                  </label>
                  <div className="relative mt-1">
                    <Asterisk className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-400" />
                    <input
                      id="family-edit-emergency-phone"
                      type="text"
                      value={emergencyPhone}
                      onChange={(e) => {
                        setEmergencyPhone(e.target.value);
                        if (errors.emergencyPhone)
                          setErrors((prev) => ({ ...prev, emergencyPhone: '' }));
                      }}
                      placeholder="0988 765 432"
                      className={`w-full rounded-xl border bg-white py-2 pr-3 pl-9 font-mono text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden focus:ring-3 focus:ring-sky-500/15 ${
                        errors.emergencyPhone ? 'border-rose-400' : 'border-slate-300'
                      }`}
                    />
                  </div>
                  {errors.emergencyPhone && (
                    <p className="mt-1 text-xs text-rose-600">{errors.emergencyPhone}</p>
                  )}
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
            disabled={isSubmitting}
            className="rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            Hủy bỏ
          </button>
          <button
            type="submit"
            disabled={isSubmitting}
            className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-6 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99] disabled:opacity-50"
          >
            <Check className="h-4 w-4 stroke-[2.5]" />
            {isSubmitting ? 'Đang lưu...' : 'Lưu thay đổi'}
          </button>
        </div>
      </form>
    </div>
  );
}

export function EditFamilyMemberModal({
  open,
  onClose,
  member,
  onSave,
}: EditFamilyMemberModalProps) {
  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      {open && member && (
        <EditFamilyMemberForm key={member.id} member={member} onClose={onClose} onSave={onSave} />
      )}
    </Modal>
  );
}
