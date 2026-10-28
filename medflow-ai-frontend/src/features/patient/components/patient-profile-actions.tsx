import { Link } from 'react-router-dom';
import { Download, Edit2, Users } from 'lucide-react';

interface PatientProfileActionsProps {
  onEditContact?: () => void;
  onDownloadPdf?: () => void;
}

export function PatientProfileActions({
  onEditContact,
  onDownloadPdf,
}: PatientProfileActionsProps) {
  return (
    <div className="mt-6 flex flex-wrap items-center justify-between gap-4 border-t border-slate-200/80 pt-6">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={onEditContact}
          className="inline-flex items-center gap-2 rounded-xl bg-sky-600 px-5 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
        >
          <Edit2 className="h-3.5 w-3.5" />
          Chỉnh sửa thông tin liên hệ
        </button>

        <Link
          to="/family"
          className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-xs font-bold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50 active:scale-[0.99]"
        >
          <Users className="h-3.5 w-3.5 text-slate-500" />
          Quản lý người thân
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
