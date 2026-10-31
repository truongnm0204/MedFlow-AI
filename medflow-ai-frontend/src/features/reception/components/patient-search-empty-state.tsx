import { SearchX } from 'lucide-react';

import { Button } from '@/shared/ui/button';

interface PatientSearchEmptyStateProps {
  query: string;
  onClear: () => void;
}

export function PatientSearchEmptyState({ query, onClear }: PatientSearchEmptyStateProps) {
  return (
    <div
      role="status"
      className="p-12 text-center rounded-3xl border border-dashed border-slate-300 bg-white shadow-2xs space-y-4"
    >
      <div className="w-14 h-14 rounded-2xl bg-slate-100 text-slate-400 flex items-center justify-center mx-auto">
        <SearchX className="w-7 h-7" aria-hidden="true" />
      </div>

      <div className="max-w-md mx-auto space-y-1.5">
        <h3 className="text-sm font-bold text-slate-800">
          Không tìm thấy bệnh nhân nào khớp với từ khóa
        </h3>
        {query && (
          <p className="text-xs text-slate-500">
            Từ khóa: <span className="font-mono font-bold text-slate-700">"{query}"</span>
          </p>
        )}
        <p className="text-xs text-slate-500 leading-relaxed">
          Vui lòng kiểm tra lại số CCCD (12 chữ số), Số điện thoại (10 chữ số) hoặc chuyển sang tìm
          kiếm bằng Họ tên tiếng Việt có dấu.
        </p>
      </div>

      <div className="flex items-center justify-center gap-3 pt-2">
        <Button variant="secondary" size="sm" onClick={onClear}>
          Xóa bộ lọc
        </Button>
      </div>
    </div>
  );
}
