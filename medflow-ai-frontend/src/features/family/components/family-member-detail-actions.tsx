import { Link } from 'react-router-dom';
import { ArrowLeft, Calendar, Download, Edit2, Trash2 } from 'lucide-react';

import type { FamilyMember } from '../types/family.types';

interface FamilyMemberDetailActionsProps {
  member: FamilyMember;
  onBookAppointment?: (member: FamilyMember) => void;
  onDownloadPdf?: () => void;
  onEditContact?: () => void;
  onRemoveMember?: () => void;
}

export function FamilyMemberDetailActions({
  member,
  onBookAppointment,
  onDownloadPdf,
  onEditContact,
  onRemoveMember,
}: FamilyMemberDetailActionsProps) {
  const canManage = member.permissions?.canManage ?? true;
  const canBook =
    (member.permissions?.canBookAppointments ?? true) &&
    (member.permissions?.relationshipStatus ?? 'ACTIVE') === 'ACTIVE';

  return (
    <div className="mt-6 flex flex-wrap items-center justify-between gap-4 border-t border-slate-200/80 pt-6">
      <div className="flex flex-wrap items-center gap-3">
        {canBook && (
          <button
            type="button"
            onClick={() => onBookAppointment?.(member)}
            className="inline-flex items-center gap-2 rounded-xl bg-sky-600 px-5 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
          >
            <Calendar className="h-3.5 w-3.5" />
            Đặt khám ngay
          </button>
        )}

        {canManage && (
          <button
            type="button"
            onClick={onEditContact}
            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-bold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50"
          >
            <Edit2 className="h-3.5 w-3.5 text-slate-500" />
            Chỉnh sửa thông tin liên hệ
          </button>
        )}

        {canManage && !member.isPrimaryAccount && (
          <button
            type="button"
            onClick={onRemoveMember}
            className="inline-flex items-center gap-2 rounded-xl border border-rose-200 bg-white px-5 py-2.5 text-xs font-bold text-rose-600 shadow-2xs transition-colors hover:border-rose-300 hover:bg-rose-50"
          >
            <Trash2 className="h-3.5 w-3.5" />
            Gỡ liên kết hồ sơ
          </button>
        )}

        <Link
          to="/family"
          className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-bold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50"
        >
          <ArrowLeft className="h-3.5 w-3.5 text-slate-500" />
          Danh sách người thân
        </Link>
      </div>

      <button
        type="button"
        onClick={onDownloadPdf}
        className="inline-flex items-center gap-2 text-xs font-bold text-sky-700 transition-colors hover:text-sky-800 hover:underline"
      >
        <Download className="h-4 w-4" />
        Tải tóm tắt hồ sơ y tế (PDF)
      </button>
    </div>
  );
}
