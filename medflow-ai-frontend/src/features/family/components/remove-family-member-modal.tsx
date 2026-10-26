import { useState } from 'react';
import { AlertCircle, AlertTriangle, Trash2, UserMinus } from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import { useRemoveFamilyMember } from '../hooks/use-remove-family-member';
import type { FamilyMember } from '../types/family.types';

interface RemoveFamilyMemberModalProps {
  open: boolean;
  onClose: () => void;
  member: FamilyMember;
  onSuccess?: () => void;
}

export function RemoveFamilyMemberModal({
  open,
  onClose,
  member,
  onSuccess,
}: RemoveFamilyMemberModalProps) {
  const removeMutation = useRemoveFamilyMember();

  const [reason, setReason] = useState('Người thân đã tự lập tài khoản riêng');
  const [confirmUnderstood, setConfirmUnderstood] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const isBlockedByAppointment = Boolean(member.hasActiveAppointment);

  const handleConfirmRemove = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isBlockedByAppointment || !confirmUnderstood) return;

    setErrorMsg(null);
    try {
      await removeMutation.mutateAsync({
        memberId: member.id,
        reason,
      });
      onSuccess?.();
      onClose();
    } catch (err: unknown) {
      setErrorMsg(err instanceof Error ? err.message : 'Không thể gỡ liên kết, vui lòng thử lại.');
    }
  };

  const isSubmitting = removeMutation.isPending;

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-xl">
      <div className="relative p-6 sm:p-7">
        {/* Header */}
        <div className="flex items-center gap-3.5 pr-8">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
            <UserMinus className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-base font-bold text-slate-900 sm:text-lg">
              Gỡ liên kết Hồ sơ Người thân
            </h3>
            <p className="mt-0.5 text-xs text-slate-500">
              Hủy ủy quyền quản lý hồ sơ khám chữa bệnh ngoại trú
            </p>
          </div>
        </div>

        {/* Member Preview Card */}
        <div className="mt-5 flex items-center gap-3.5 rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
          <img
            src={member.avatarUrl}
            alt={member.fullName}
            className="h-12 w-12 rounded-full border border-slate-200 object-cover"
          />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-bold text-slate-900 truncate">{member.fullName}</span>
              <span className="rounded-md border border-slate-200 bg-white px-2 py-0.5 text-[11px] font-semibold text-slate-700">
                {member.relationship}
              </span>
            </div>
            <p className="mt-0.5 text-xs text-slate-500 truncate">{member.subStatus}</p>
          </div>
        </div>

        {/* Error banner if any */}
        {errorMsg && (
          <div className="mt-4 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs text-rose-700">
            <AlertCircle className="h-4 w-4 shrink-0" />
            <span>{errorMsg}</span>
          </div>
        )}

        {/* Case 1: Blocked by active appointment constraint */}
        {isBlockedByAppointment ? (
          <div className="mt-5 space-y-4">
            <div className="flex items-start gap-3 rounded-2xl border border-rose-200 bg-rose-50/80 p-4 text-xs leading-relaxed text-rose-900">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-rose-600" />
              <div>
                <strong className="font-bold text-rose-950">Không thể gỡ liên kết lúc này:</strong>
                <p className="mt-1">
                  Người thân đang có lịch hẹn khám sắp tới vào{' '}
                  <span className="font-semibold text-rose-950">
                    {member.activeAppointmentDetails ?? 'Lịch khám đang chờ xử lý'}
                  </span>
                  .
                </p>
                <p className="mt-1 text-rose-800">
                  Theo quy định y tế, vui lòng hoàn tất đợt khám hoặc hủy lịch hẹn trước khi gỡ hồ
                  sơ người thân.
                </p>
              </div>
            </div>

            <div className="flex items-center justify-end gap-3 border-t border-slate-100 pt-4">
              <button
                type="button"
                onClick={onClose}
                className="rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
              >
                Đã hiểu & Quay lại
              </button>
            </div>
          </div>
        ) : (
          /* Case 2: Eligible to unlink */
          <form onSubmit={handleConfirmRemove} className="mt-5 space-y-4">
            {/* Legal Notice */}
            <div className="flex items-start gap-3 rounded-2xl border border-amber-200/80 bg-amber-50/70 p-3.5 text-xs text-amber-900 leading-relaxed">
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
              <p>
                <strong>Lưu ý bảo mật EHR:</strong> Toàn bộ lịch sử bệnh án và kết quả xét nghiệm đã
                khám trước đây vẫn được lưu trữ vĩnh viễn trên hệ thống bệnh viện theo quy định Bộ Y
                Tế. Thao tác này chỉ gỡ quyền đặt lịch và theo dõi thay.
              </p>
            </div>

            {/* Reason Select */}
            <div>
              <label htmlFor="unlink-reason" className="block text-xs font-semibold text-slate-700">
                Lý do gỡ liên kết <span className="text-rose-500">*</span>
              </label>
              <select
                id="unlink-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="mt-1.5 w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-medium text-slate-900 shadow-2xs focus:border-sky-500 focus:outline-hidden"
              >
                <option value="Người thân đã tự lập tài khoản riêng">
                  Người thân đã tự lập tài khoản riêng
                </option>
                <option value="Không còn nhu cầu quản lý thay">
                  Không còn nhu cầu quản lý thay
                </option>
                <option value="Thông tin hồ sơ tạo nhầm">Thông tin hồ sơ tạo nhầm</option>
                <option value="Khác">Khác</option>
              </select>
            </div>

            {/* Confirmation Checkbox */}
            <label className="flex items-start gap-2.5 cursor-pointer pt-1">
              <input
                type="checkbox"
                checked={confirmUnderstood}
                onChange={(e) => setConfirmUnderstood(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-slate-300 text-rose-600 focus:ring-rose-500"
              />
              <span className="text-xs text-slate-700 leading-relaxed">
                Tôi xác nhận muốn gỡ liên kết hồ sơ của{' '}
                <strong className="text-slate-900">{member.fullName}</strong> và hiểu rằng tôi sẽ
                không thể đặt lịch hoặc xem kết quả khám bệnh của người thân này nữa.
              </span>
            </label>

            {/* Footer Buttons */}
            <div className="mt-6 flex items-center justify-end gap-3 border-t border-slate-100 pt-4">
              <button
                type="button"
                onClick={onClose}
                disabled={isSubmitting}
                className="rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
              >
                Hủy bỏ
              </button>
              <button
                type="submit"
                disabled={!confirmUnderstood || isSubmitting}
                className="inline-flex items-center gap-1.5 rounded-xl bg-rose-600 px-5 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-rose-700 active:scale-[0.99] disabled:cursor-not-allowed disabled:opacity-50"
              >
                <Trash2 className="h-4 w-4" />
                {isSubmitting ? 'Đang gỡ...' : 'Xác nhận gỡ liên kết'}
              </button>
            </div>
          </form>
        )}
      </div>
    </Modal>
  );
}
