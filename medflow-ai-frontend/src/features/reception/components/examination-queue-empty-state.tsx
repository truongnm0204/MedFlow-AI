import { ListX } from 'lucide-react';

import { Button } from '@/shared/ui/button';

export interface ExaminationQueueEmptyStateProps {
  keyword: string;
  onReset: () => void;
}

export function ExaminationQueueEmptyState({ keyword, onReset }: ExaminationQueueEmptyStateProps) {
  return (
    <div
      role="status"
      className="bg-white rounded-2xl border border-border p-10 text-center shadow-sm space-y-3"
    >
      <div className="w-12 h-12 rounded-2xl bg-canvas border border-border flex items-center justify-center mx-auto text-sub">
        <ListX className="w-6 h-6" aria-hidden="true" />
      </div>
      <div className="text-sm font-bold text-navy">
        Không có lượt khám nào trong hàng đợi khớp với bộ lọc
      </div>
      <p className="text-xs text-sub max-w-md mx-auto">
        {keyword
          ? `Không tìm thấy bệnh nhân hoặc số phiếu khớp với từ khóa "${keyword}".`
          : 'Hiện không có lượt khám nào thỏa mãn tiêu chí phòng khám hoặc trạng thái đang chọn.'}
      </p>
      <div className="pt-1">
        <Button variant="secondary" size="sm" onClick={onReset}>
          Xóa bộ lọc
        </Button>
      </div>
    </div>
  );
}
