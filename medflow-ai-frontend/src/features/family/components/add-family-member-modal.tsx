import { useState } from 'react';
import { UserPlus } from 'lucide-react';
import { Modal } from '@/shared/ui/modal';
import type { FamilyMember, RelationshipType } from '../types/family.types';

interface AddFamilyMemberModalProps {
  open: boolean;
  onClose: () => void;
  onAddMember: (newMember: FamilyMember) => void;
}

export function AddFamilyMemberModal({ open, onClose, onAddMember }: AddFamilyMemberModalProps) {
  const [fullName, setFullName] = useState('');
  const [relationship, setRelationship] = useState<RelationshipType>('Con trai');
  const [birthDate, setBirthDate] = useState('');
  const [gender, setGender] = useState<'Nam' | 'Nữ'>('Nam');
  const [phone, setPhone] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    const member: FamilyMember = {
      id: `FAM-${Date.now()}`,
      fullName: fullName.toUpperCase(),
      relationship,
      relationshipBadgeColor:
        relationship === 'Con trai' || relationship === 'Con gái'
          ? 'blue'
          : relationship === 'Vợ' || relationship === 'Chồng'
            ? 'rose'
            : 'amber',
      patientCode: `#PAT-2026-${Math.floor(1000 + Math.random() * 9000)}`,
      subStatus: 'Hồ sơ người thân mới',
      isPrimaryAccount: false,
      avatarUrl:
        gender === 'Nam'
          ? 'https://images.unsplash.com/photo-1543332164-6e82f355badc?w=300&auto=format&fit=crop&q=80'
          : 'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=300&auto=format&fit=crop&q=80',
      birthDate: birthDate || '01/01/2020',
      age: 6,
      gender,
      phone: phone || '0912 345 678',
    };

    onAddMember(member);
    onClose();
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-xl">
      <form onSubmit={handleSubmit} className="p-6">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-50 text-sky-600">
            <UserPlus className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-base font-bold text-slate-900">
              Thêm hồ sơ người thân trong gia đình
            </h3>
            <p className="text-xs text-slate-400">
              Liên kết hồ sơ để thuận tiện đặt lịch và tra cứu kết quả khám
            </p>
          </div>
        </div>

        <div className="mt-5 space-y-4">
          <div>
            <label className="block text-xs font-semibold text-slate-700">
              Họ và tên người thân <span className="text-rose-500">*</span>
            </label>
            <input
              type="text"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              required
              placeholder="VD: NGUYỄN VĂN B"
              className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Mối quan hệ <span className="text-rose-500">*</span>
              </label>
              <select
                value={relationship}
                onChange={(e) => setRelationship(e.target.value as RelationshipType)}
                className="mt-1.5 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden"
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

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Giới tính <span className="text-rose-500">*</span>
              </label>
              <select
                value={gender}
                onChange={(e) => setGender(e.target.value as 'Nam' | 'Nữ')}
                className="mt-1.5 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden"
              >
                <option value="Nam">Nam</option>
                <option value="Nữ">Nữ</option>
              </select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Ngày sinh (DD/MM/YYYY)
              </label>
              <input
                type="text"
                value={birthDate}
                onChange={(e) => setBirthDate(e.target.value)}
                placeholder="15/08/2015"
                className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700">
                Số điện thoại liên hệ
              </label>
              <input
                type="text"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="0912..."
                className="mt-1.5 w-full rounded-xl border border-slate-300 px-3.5 py-2 font-mono text-xs font-medium text-slate-900 focus:border-sky-500 focus:outline-hidden"
              />
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
            className="rounded-xl bg-sky-600 px-5 py-2 text-xs font-bold text-white shadow-sm hover:bg-sky-700"
          >
            Thêm người thân
          </button>
        </div>
      </form>
    </Modal>
  );
}
