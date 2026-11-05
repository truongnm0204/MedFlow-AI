import { RotateCcw, Search, X } from 'lucide-react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { DEFAULT_QUEUE_DATE } from '../data/examination-queue-mock';
import type { ExaminationQueueFilterFormValues } from '../schemas/examination-queue-filter.schema';
import type {
  ExaminationQueuePriorityFilter,
  ExaminationQueueStatusFilter,
  ExaminationVisitTypeFilter,
  QueueArrivalClassFilter,
} from '../types/examination-queue.types';

export interface ExaminationQueueFilterBarProps {
  filters: ExaminationQueueFilterFormValues;
  servicePointOptions: Array<{ id: string; label: string; roomCode: string; count: number }>;
  statusCounts: Record<ExaminationQueueStatusFilter, number>;
  validationError: string | null;
  onFilterChange: (patch: Partial<ExaminationQueueFilterFormValues>) => void;
  onReset: () => void;
}

const STATUS_TABS: Array<{ id: ExaminationQueueStatusFilter; label: string }> = [
  { id: 'ALL', label: 'Tất cả trạng thái' },
  { id: 'IN_PROGRESS', label: 'Đang khám' },
  { id: 'CALLED', label: 'Đã gọi số' },
  { id: 'READY', label: 'Sẵn sàng' },
  { id: 'WAITING', label: 'Đang chờ' },
  { id: 'COMPLETED', label: 'Hoàn tất' },
  { id: 'SKIPPED', label: 'Qua lượt' },
];

export function ExaminationQueueFilterBar({
  filters,
  servicePointOptions,
  statusCounts,
  validationError,
  onFilterChange,
  onReset,
}: ExaminationQueueFilterBarProps) {
  const hasActiveFilters =
    filters.servicePointId !== 'ALL' ||
    filters.status !== 'ALL' ||
    filters.priority !== 'ALL' ||
    filters.visitType !== 'ALL' ||
    filters.arrivalClass !== 'ALL' ||
    filters.keyword.trim() !== '' ||
    filters.queueDate !== DEFAULT_QUEUE_DATE;

  return (
    <div className="bg-white rounded-2xl border border-border p-4 shadow-sm space-y-4">
      {/* Row 1: Search & Secondary Selects */}
      <div className="flex flex-col lg:flex-row gap-3">
        <div className="relative flex-1">
          <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-sub">
            <Search className="w-4 h-4" aria-hidden="true" />
          </div>
          <input
            type="text"
            aria-label="Tìm kiếm trong hàng đợi khám"
            value={filters.keyword}
            onChange={(e) => onFilterChange({ keyword: e.target.value })}
            placeholder="Tìm theo Số thứ tự (A012), Mã BN (BN-...), Mã lượt khám (VIS-...) hoặc Họ tên..."
            className="w-full rounded-xl border border-border bg-canvas/60 pl-10 pr-9 py-2.5 text-xs text-ink placeholder:text-sub focus:bg-white focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15 transition-all"
          />
          {filters.keyword && (
            <button
              type="button"
              onClick={() => onFilterChange({ keyword: '' })}
              aria-label="Xóa từ khóa tìm kiếm"
              className="absolute inset-y-0 right-0 flex items-center pr-3 text-sub hover:text-ink"
            >
              <X className="w-4 h-4" aria-hidden="true" />
            </button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <select
            aria-label="Lọc theo độ ưu tiên"
            value={filters.priority}
            onChange={(e) =>
              onFilterChange({ priority: e.target.value as ExaminationQueuePriorityFilter })
            }
            className="rounded-xl border border-border bg-white px-3 py-2 text-xs font-medium text-ink focus:border-primary focus:outline-none"
          >
            <option value="ALL">Mọi mức ưu tiên</option>
            <option value="URGENT">Khẩn cấp</option>
            <option value="ELDERLY">Người cao tuổi</option>
            <option value="PREGNANT">Thai phụ</option>
            <option value="RETURN_RESULT">Trả KQ CLS</option>
            <option value="NORMAL">Bình thường</option>
          </select>

          <select
            aria-label="Lọc theo phân loại khám"
            value={filters.visitType}
            onChange={(e) =>
              onFilterChange({ visitType: e.target.value as ExaminationVisitTypeFilter })
            }
            className="rounded-xl border border-border bg-white px-3 py-2 text-xs font-medium text-ink focus:border-primary focus:outline-none"
          >
            <option value="ALL">Mọi hình thức khám</option>
            <option value="SCHEDULED">Hẹn trước</option>
            <option value="WALK_IN">Walk-in</option>
          </select>

          <select
            aria-label="Lọc theo phân loại đến"
            value={filters.arrivalClass}
            onChange={(e) =>
              onFilterChange({ arrivalClass: e.target.value as QueueArrivalClassFilter })
            }
            className="rounded-xl border border-border bg-white px-3 py-2 text-xs font-medium text-ink focus:border-primary focus:outline-none"
          >
            <option value="ALL">Mọi phân loại đến</option>
            <option value="EARLY">Đến sớm (Early)</option>
            <option value="ON_TIME_OR_GRACE">Đúng giờ</option>
            <option value="LATE">Đến muộn</option>
            <option value="WALK_IN">Vãng lai (Walk-in)</option>
          </select>

          <input
            type="date"
            aria-label="Ngày hàng đợi"
            value={filters.queueDate}
            onChange={(e) => onFilterChange({ queueDate: e.target.value })}
            className="rounded-xl border border-border bg-white px-3 py-2 text-xs font-mono text-ink focus:border-primary focus:outline-none"
          />

          {hasActiveFilters && (
            <Button variant="secondary" size="sm" onClick={onReset}>
              <RotateCcw className="w-3.5 h-3.5" aria-hidden="true" />
              Đặt lại bộ lọc
            </Button>
          )}
        </div>
      </div>

      {/* Row 2: Service point pills */}
      <div
        role="tablist"
        aria-label="Lọc theo điểm dịch vụ phòng khám"
        className="flex flex-wrap items-center gap-1.5 pt-2 border-t border-border"
      >
        {servicePointOptions.map((option) => {
          const isActive = filters.servicePointId === option.id;
          return (
            <button
              key={option.id}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => onFilterChange({ servicePointId: option.id })}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
                isActive ? 'bg-primary text-white shadow-xs' : 'bg-canvas text-sub hover:text-ink'
              }`}
            >
              <span>{option.label}</span>
              <span
                className={`rounded-full px-1.5 py-0.5 text-[10px] font-mono font-bold leading-none ${
                  isActive ? 'bg-white/20 text-white' : 'bg-white text-sub'
                }`}
              >
                {option.count}
              </span>
            </button>
          );
        })}
      </div>

      {/* Row 3: Status filter tabs */}
      <div
        role="tablist"
        aria-label="Lọc theo trạng thái hàng đợi"
        className="flex flex-wrap items-center gap-1.5 pt-2 border-t border-border"
      >
        {STATUS_TABS.map((tab) => {
          const isActive = filters.status === tab.id;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => onFilterChange({ status: tab.id })}
              className={`inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold transition-colors ${
                isActive ? 'bg-navy text-white shadow-xs' : 'bg-canvas text-sub hover:text-ink'
              }`}
            >
              <span>{tab.label}</span>
              <span
                className={`rounded-full px-1.5 py-0.5 text-[10px] font-mono font-bold leading-none ${
                  isActive ? 'bg-white/20 text-white' : 'bg-white text-sub'
                }`}
              >
                {statusCounts[tab.id] ?? 0}
              </span>
            </button>
          );
        })}
      </div>

      {validationError && (
        <div role="alert">
          <Alert tone="error">{validationError}</Alert>
        </div>
      )}
    </div>
  );
}
