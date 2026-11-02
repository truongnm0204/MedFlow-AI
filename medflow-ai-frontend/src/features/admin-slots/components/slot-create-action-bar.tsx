import { PlusCircle, RotateCcw } from 'lucide-react';

interface SlotCreateActionBarProps {
  onCancel: () => void;
  onSaveDraft: () => void;
  isSubmitting?: boolean;
}

export function SlotCreateActionBar({
  onCancel,
  onSaveDraft,
  isSubmitting = false,
}: SlotCreateActionBarProps) {
  return (
    <div
      className="fixed inset-x-0 bottom-0 z-40 border-t border-border bg-white/95 backdrop-blur-xs py-3 px-6 shadow-lg"
      role="region"
      aria-label="Thao tác tạo khung giờ khám"
    >
      <div className="mx-auto flex max-w-[1720px] flex-wrap items-center justify-between gap-4">
        {/* Left Operational Note */}
        <div className="flex items-center gap-2 text-xs text-slate-600">
          <RotateCcw className="h-4 w-4 text-sky-600 shrink-0" aria-hidden="true" />
          <span>Đóng slot chỉ ngăn lượt đặt mới; lịch đã đặt được giữ nguyên.</span>
        </div>

        {/* Right Action Buttons */}
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onCancel}
            className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-medium text-slate-700 shadow-2xs hover:bg-slate-50 hover:text-slate-900 transition-colors cursor-pointer"
          >
            Hủy bỏ <span className="text-slate-400 ml-1">[Esc]</span>
          </button>

          <button
            type="button"
            onClick={onSaveDraft}
            className="rounded-lg border border-sky-200 bg-sky-50 px-4 py-2 text-xs font-semibold text-primary shadow-2xs hover:bg-sky-100 transition-colors cursor-pointer"
          >
            Lưu bản nháp
          </button>

          <button
            type="submit"
            id="slot-create-submit"
            disabled={isSubmitting}
            className="flex items-center gap-2 rounded-lg bg-primary px-5 py-2 text-xs font-semibold text-white shadow-xs hover:bg-primary/90 focus:outline-hidden focus:ring-2 focus:ring-primary/40 disabled:opacity-50 transition-colors cursor-pointer"
          >
            <PlusCircle className="h-4 w-4" aria-hidden="true" />
            <span>{isSubmitting ? 'Đang lưu...' : 'Lưu khung giờ'}</span>
          </button>
        </div>
      </div>
    </div>
  );
}
