import { Plus, UserPlus } from 'lucide-react';

interface FamilyEmptySlotCardProps {
  onAddMember?: () => void;
}

export function FamilyEmptySlotCard({ onAddMember }: FamilyEmptySlotCardProps) {
  return (
    <div className="mt-6 rounded-2xl border-2 border-dashed border-slate-300 bg-white/60 p-8 text-center transition-colors hover:border-slate-400">
      <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-full border border-slate-200 bg-white text-sky-600 shadow-2xs">
        <Plus className="h-5 w-5 stroke-[2.5]" />
      </div>

      <h4 className="mt-3 text-sm font-bold text-slate-800">
        Thêm hồ sơ người thân mới
      </h4>

      <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-slate-400">
        Liên kết hồ sơ cha mẹ, vợ/chồng hoặc con cái để thuận tiện đăng ký lịch khám bệnh chuyên
        khoa ngoại trú và nhận nhắc nhở điều trị.
      </p>

      <button
        type="button"
        onClick={onAddMember}
        className="mt-4 inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-2xs transition-colors hover:bg-slate-50 active:scale-[0.99]"
      >
        <UserPlus className="h-3.5 w-3.5 text-slate-500" />
        Thêm người thân ngay
      </button>
    </div>
  );
}
