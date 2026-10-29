import { Stethoscope, RotateCcw } from 'lucide-react';

interface ServiceEmptyStateProps {
  onReset: () => void;
}

export function ServiceEmptyState({ onReset }: ServiceEmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center p-12 bg-white rounded-2xl border border-dashed border-slate-300 text-center">
      <div className="w-14 h-14 bg-sky-50 text-sky-600 rounded-2xl flex items-center justify-center mb-4">
        <Stethoscope className="w-7 h-7" />
      </div>
      <h3 className="text-base font-semibold text-slate-800 mb-1">
        Không tìm thấy dịch vụ khám phù hợp
      </h3>
      <p className="text-xs text-slate-500 max-w-sm mb-6">
        Vui lòng thử tìm kiếm với từ khóa khác, hoặc đặt lại bộ lọc để xem toàn bộ danh mục dịch vụ
        khám.
      </p>
      <button
        type="button"
        onClick={onReset}
        className="inline-flex items-center gap-2 px-4 py-2 bg-sky-600 hover:bg-sky-700 text-white text-xs font-medium rounded-xl transition-colors shadow-sm"
      >
        <RotateCcw className="w-4 h-4" />
        <span>Đặt lại toàn bộ bộ lọc</span>
      </button>
    </div>
  );
}
