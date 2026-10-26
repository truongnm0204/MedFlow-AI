import { useState } from 'react';
import {
  AlertCircle,
  Calendar,
  Check,
  CreditCard,
  Flag,
  Info,
  Mail,
  MapPin,
  Phone,
  PhoneCall,
  User,
  UserPlus,
  Users,
} from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import { useCreateFamilyMember } from '../hooks/use-create-family-member';
import { createFamilyMemberSchema, type CreateFamilyMemberInput } from '../schemas/family-member.schema';
import type { FamilyMember, RelationshipType } from '../types/family.types';

interface AddFamilyMemberModalProps {
  open: boolean;
  onClose: () => void;
  onAddMember: (newMember: FamilyMember) => void;
  isSubmitting?: boolean;
}

export function AddFamilyMemberModal({
  open,
  onClose,
  onAddMember,
  isSubmitting = false,
}: AddFamilyMemberModalProps) {
  const createMutation = useCreateFamilyMember();

  const [fullName, setFullName] = useState('');
  const [relationship, setRelationship] = useState<RelationshipType>('Con trai');
  const [birthDate, setBirthDate] = useState('');
  const [gender, setGender] = useState<'Nam' | 'Nữ'>('Nam');
  const [identificationNo, setIdentificationNo] = useState('');
  const [nationality, setNationality] = useState('Việt Nam');
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const [address, setAddress] = useState('Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Hà Nội');
  const [emergencyContactName, setEmergencyContactName] = useState('Nguyễn Văn An');
  const emergencyContactRelation = 'Bố ruột (Giám hộ)';
  const [emergencyPhone, setEmergencyPhone] = useState('0912 345 678');
  const [validationError, setValidationError] = useState<string | null>(null);

  const isPending = isSubmitting || createMutation.isPending;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const rawInput: CreateFamilyMemberInput = {
      fullName: fullName.trim(),
      relationship,
      gender,
      birthDate: birthDate.trim(),
      identificationNo: identificationNo.trim() || undefined,
      nationality: nationality.trim() || 'Việt Nam',
      phone: phone.trim() || undefined,
      email: email.trim() || undefined,
      address: address.trim(),
      emergencyContactName: emergencyContactName.trim() || undefined,
      emergencyContactRelation: emergencyContactRelation.trim() || undefined,
      emergencyPhone: emergencyPhone.trim() || undefined,
    };

    const result = createFamilyMemberSchema.safeParse(rawInput);
    if (!result.success) {
      const firstIssue = result.error.issues[0];
      setValidationError(firstIssue?.message ?? 'Vui lòng kiểm tra lại thông tin nhập.');
      return;
    }

    try {
      const created = await createMutation.mutateAsync(result.data);
      onAddMember(created);
      onClose();
    } catch (err: unknown) {
      setValidationError(err instanceof Error ? err.message : 'Có lỗi khi thêm người thân.');
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form onSubmit={handleSubmit} className="p-6 sm:p-7" noValidate>
        {/* Header */}
        <div className="flex items-center gap-3.5 pr-8">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-sky-50 text-sky-600">
            <UserPlus className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-lg font-bold tracking-tight text-slate-900">
              Thêm Hồ sơ Người thân
            </h2>
            <p className="mt-0.5 text-xs text-slate-500">
              Liên kết hồ sơ khám chữa bệnh ngoại trú trong gia đình
            </p>
          </div>
        </div>

        {/* Notice Box matching reference design */}
        <div className="mt-5 flex items-start gap-3 rounded-2xl border border-sky-100 bg-[#f0f9ff] p-4 text-xs leading-relaxed text-sky-900">
          <Info className="mt-0.5 h-4 w-4 shrink-0 text-sky-600" />
          <p>
            <span className="font-bold">Lưu ý:</span> Để đảm bảo an toàn pháp lý của Hồ sơ bệnh án
            điện tử, thông tin định danh của người thân cần chính xác và trùng khớp với CCCD hoặc
            Giấy khai sinh khi đối soát tại quầy tiếp đón.
          </p>
        </div>

        {validationError && (
          <div
            role="alert"
            className="mt-4 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs font-medium text-rose-700"
          >
            <AlertCircle className="h-4 w-4 shrink-0" />
            <span>{validationError}</span>
          </div>
        )}

        {/* Section 1: Thông tin định danh */}
        <div className="mt-6">
          <h3 className="text-xs font-bold tracking-wider text-slate-500 uppercase">
            Thông tin định danh người thân
          </h3>

          <div className="mt-3 grid grid-cols-1 gap-3.5 sm:grid-cols-2">
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Họ và tên người thân <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5 flex items-center">
                <User className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  placeholder="VD: NGUYỄN VĂN B"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold uppercase text-slate-900 placeholder:normal-case placeholder:font-normal placeholder:text-slate-400 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Mối quan hệ <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5 flex items-center">
                <Users className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <select
                  value={relationship}
                  onChange={(e) => setRelationship(e.target.value as RelationshipType)}
                  className="w-full rounded-xl border border-slate-300 bg-white py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 focus:border-sky-500 focus:outline-hidden"
                >
                  <option value="Con trai">Con trai</option>
                  <option value="Con gái">Con gái</option>
                  <option value="Vợ">Vợ</option>
                  <option value="Chồng">Chồng</option>
                  <option value="Mẹ ruột">Mẹ ruột</option>
                  <option value="Bố ruột">Bố ruột</option>
                  <option value="Khác">Khác</option>
                </select>
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Ngày sinh (DD/MM/YYYY) <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5 flex items-center">
                <Calendar className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={birthDate}
                  onChange={(e) => setBirthDate(e.target.value)}
                  placeholder="15/08/2018"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 placeholder:font-normal placeholder:text-slate-400 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Giới tính <span className="text-rose-500">*</span>
              </label>
              <div className="mt-1.5 flex items-center gap-3 pt-1">
                <label className="inline-flex cursor-pointer items-center gap-2 text-xs font-semibold text-slate-800">
                  <input
                    type="radio"
                    name="gender"
                    value="Nam"
                    checked={gender === 'Nam'}
                    onChange={() => setGender('Nam')}
                    className="h-4 w-4 text-sky-600 focus:ring-sky-500"
                  />
                  <span>Nam</span>
                </label>
                <label className="inline-flex cursor-pointer items-center gap-2 text-xs font-semibold text-slate-800">
                  <input
                    type="radio"
                    name="gender"
                    value="Nữ"
                    checked={gender === 'Nữ'}
                    onChange={() => setGender('Nữ')}
                    className="h-4 w-4 text-sky-600 focus:ring-sky-500"
                  />
                  <span>Nữ</span>
                </label>
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Số CCCD / Giấy khai sinh
              </label>
              <div className="relative mt-1.5 flex items-center">
                <CreditCard className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={identificationNo}
                  onChange={(e) => setIdentificationNo(e.target.value)}
                  placeholder="VD: 001218... (nếu có)"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 placeholder:font-normal placeholder:text-slate-400 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">Quốc tịch</label>
              <div className="relative mt-1.5 flex items-center">
                <Flag className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={nationality}
                  onChange={(e) => setNationality(e.target.value)}
                  placeholder="Việt Nam"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>
          </div>
        </div>

        {/* Section 2: Tiếp nhận kết quả & Liên hệ */}
        <div className="mt-6">
          <h3 className="text-xs font-bold tracking-wider text-slate-500 uppercase">
            Thông tin tiếp nhận kết quả & liên hệ
          </h3>

          <div className="mt-3 space-y-3.5">
            <div>
              <div className="flex items-center justify-between">
                <label className="block text-xs font-semibold text-slate-700">
                  Số điện thoại liên hệ
                </label>
                <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200/80 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                  <Check className="h-3 w-3 stroke-[2.5]" />
                  Nhận SMS thông báo
                </span>
              </div>
              <div className="relative mt-1.5 flex items-center">
                <Phone className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="VD: 0912 345 678 (để trống sẽ dùng SĐT của bạn)"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 font-mono text-xs font-semibold text-slate-900 placeholder:font-normal placeholder:text-slate-400 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Email nhận kết quả xét nghiệm / đơn thuốc
              </label>
              <div className="relative mt-1.5 flex items-center">
                <Mail className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="VD: nguyenvana.patient@gmail.com"
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 placeholder:font-normal placeholder:text-slate-400 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
              <p className="mt-1 text-[11px] text-slate-400">
                File kết quả xét nghiệm định dạng PDF kèm chữ ký số bảo mật sẽ được gửi về hộp thư này.
              </p>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Địa chỉ thường trú / nơi ở hiện tại <span className="text-rose-500">*</span>
              </label>
              <div className="relative mt-1.5 flex items-center">
                <MapPin className="absolute left-3.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  value={address}
                  onChange={(e) => setAddress(e.target.value)}
                  className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 focus:border-sky-500 focus:outline-hidden"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Thông tin người liên hệ khẩn cấp (Emergency Contact)
              </label>
              <div className="mt-1.5 grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="relative flex items-center">
                  <Users className="absolute left-3.5 h-4 w-4 text-slate-400" />
                  <input
                    type="text"
                    value={emergencyContactName}
                    onChange={(e) => setEmergencyContactName(e.target.value)}
                    placeholder="Họ tên người giám hộ / liên hệ"
                    className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 text-xs font-semibold text-slate-900 focus:border-sky-500 focus:outline-hidden"
                  />
                </div>

                <div className="relative flex items-center">
                  <PhoneCall className="absolute left-3.5 h-4 w-4 text-slate-400" />
                  <input
                    type="text"
                    value={emergencyPhone}
                    onChange={(e) => setEmergencyPhone(e.target.value)}
                    placeholder="Số điện thoại khẩn cấp"
                    className="w-full rounded-xl border border-slate-300 py-2.5 pr-3.5 pl-10 font-mono text-xs font-semibold text-slate-900 focus:border-sky-500 focus:outline-hidden"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Footer Actions matching reference button styles */}
        <div className="mt-7 flex items-center justify-end gap-3 border-t border-slate-100 pt-4">
          <button
            type="button"
            onClick={onClose}
            disabled={isPending}
            className="rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 shadow-2xs hover:bg-slate-50 disabled:opacity-50"
          >
            Hủy bỏ
          </button>
          <button
            type="submit"
            disabled={isPending}
            className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-6 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99] disabled:opacity-50"
          >
            <Check className="h-4 w-4 stroke-[2.5]" />
            {isPending ? 'Đang lưu...' : 'Thêm người thân'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
