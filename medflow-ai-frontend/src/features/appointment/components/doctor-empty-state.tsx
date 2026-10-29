import { RotateCcw, UserX } from 'lucide-react';

import { Button } from '@/shared/ui/button';

interface DoctorEmptyStateProps {
  onResetFilters: () => void;
}

export function DoctorEmptyState({ onResetFilters }: DoctorEmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-slate-200 bg-white p-12 text-center shadow-sm">
      <div className="flex h-14 w-14 items-center justify-center rounded-full bg-slate-100 text-slate-400">
        <UserX className="h-7 w-7" aria-hidden="true" />
      </div>
      <h3 className="mt-4 text-base font-semibold text-slate-900">Không tìm thấy bác sĩ phù hợp</h3>
      <p className="mt-1 max-w-md text-xs text-slate-500 leading-relaxed">
        Không có bác sĩ chuyên khoa nào khớp với tiêu chí tìm kiếm hoặc bộ lọc hiện tại của bạn. Vui
        lòng thử từ khóa khác hoặc đặt lại bộ lọc.
      </p>
      <Button
        type="button"
        variant="secondary"
        onClick={onResetFilters}
        className="mt-5 gap-2 text-xs font-medium text-slate-700"
      >
        <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
        <span>Đặt lại toàn bộ bộ lọc</span>
      </Button>
    </div>
  );
}
