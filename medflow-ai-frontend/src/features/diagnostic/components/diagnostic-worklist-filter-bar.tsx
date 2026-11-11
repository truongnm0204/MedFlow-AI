import { RotateCcw, Search } from 'lucide-react';

import { Alert } from '@/shared/ui/alert';

import type { DiagnosticWorklistFilterFormValues } from '../schemas/diagnostic-worklist-filter.schema';
import type {
  DiagnosticServicePointWorklistDto,
  DiagnosticWorklistStatsDto,
} from '../types/diagnostic-worklist.types';

interface DiagnosticWorklistFilterBarProps {
  filters: DiagnosticWorklistFilterFormValues;
  validationError: string | null;
  stats: DiagnosticWorklistStatsDto;
  servicePoints: DiagnosticServicePointWorklistDto[];
  onFilterChange: (patch: Partial<DiagnosticWorklistFilterFormValues>) => void;
  onReset: () => void;
}

const STATUS_TABS: Array<{
  value: DiagnosticWorklistFilterFormValues['status'];
  label: string;
  countKey?: keyof DiagnosticWorklistStatsDto;
}> = [
  { value: 'ALL', label: 'Tất cả', countKey: 'totalOrders' },
  { value: 'ORDERED', label: 'Chờ xác nhận', countKey: 'pendingOrders' },
  { value: 'CONFIRMED', label: 'Đã xác nhận', countKey: 'confirmedOrders' },
  { value: 'IN_PROGRESS', label: 'Đang thực hiện', countKey: 'inExecution' },
  { value: 'COMPLETED', label: 'Hoàn tất', countKey: 'completedOrders' },
  { value: 'CANCELLED', label: 'Đã hủy', countKey: 'cancelledOrders' },
];

export function DiagnosticWorklistFilterBar({
  filters,
  validationError,
  stats,
  servicePoints,
  onFilterChange,
  onReset,
}: DiagnosticWorklistFilterBarProps) {
  return (
    <div className="space-y-3">
      <div
        className="flex flex-wrap items-center justify-between gap-2"
        role="group"
        aria-label="Lọc nhanh trạng thái chỉ định CLS"
      >
        <div className="flex flex-wrap items-center gap-1.5">
          {STATUS_TABS.map((tab) => {
            const isSelected = filters.status === tab.value;
            const count = tab.countKey ? stats[tab.countKey] : undefined;
            return (
              <button
                key={tab.value}
                type="button"
                aria-pressed={isSelected}
                onClick={() => onFilterChange({ status: tab.value, page: 1 })}
                className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold transition-colors ${
                  isSelected
                    ? 'border-primary bg-primary text-white shadow-2xs'
                    : 'border-border bg-white text-navy hover:bg-canvas'
                }`}
              >
                <span>{tab.label}</span>
                {typeof count === 'number' && (
                  <span
                    className={`rounded-full px-1.5 py-0.2 font-mono text-[10px] font-bold ${
                      isSelected ? 'bg-white/20 text-white' : 'bg-canvas text-sub'
                    }`}
                  >
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        <button
          type="button"
          onClick={onReset}
          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-sub transition-colors hover:bg-canvas hover:text-navy"
        >
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Đặt lại bộ lọc</span>
        </button>
      </div>

      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 lg:grid-cols-6">
        <div className="relative sm:col-span-2">
          <Search
            className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
            aria-hidden="true"
          />
          <input
            type="search"
            value={filters.keyword}
            onChange={(e) => onFilterChange({ keyword: e.target.value, page: 1 })}
            placeholder="Tìm mã CĐ, số phiếu, tên BN, mã BN, dịch vụ, BS..."
            aria-label="Tìm kiếm danh sách công việc CLS"
            className="h-9 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
          />
        </div>

        <select
          value={filters.category}
          onChange={(e) =>
            onFilterChange({
              category: e.target.value as DiagnosticWorklistFilterFormValues['category'],
              page: 1,
            })
          }
          aria-label="Lọc nhóm cận lâm sàng"
          className="h-9 rounded-lg border border-border bg-canvas px-2.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
        >
          <option value="ALL">Tất cả nhóm CLS</option>
          <option value="LABORATORY">Xét nghiệm</option>
          <option value="IMAGING">Chẩn đoán hình ảnh</option>
          <option value="FUNCTIONAL">Thăm dò chức năng</option>
        </select>

        <select
          value={filters.priority}
          onChange={(e) =>
            onFilterChange({
              priority: e.target.value as DiagnosticWorklistFilterFormValues['priority'],
              page: 1,
            })
          }
          aria-label="Lọc mức độ ưu tiên"
          className="h-9 rounded-lg border border-border bg-canvas px-2.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
        >
          <option value="ALL">Mọi mức ưu tiên</option>
          <option value="STAT">Cấp cứu (STAT)</option>
          <option value="URGENT">Khẩn (URGENT)</option>
          <option value="NORMAL">Thường quy</option>
        </select>

        <select
          value={filters.servicePointId}
          onChange={(e) => onFilterChange({ servicePointId: e.target.value, page: 1 })}
          aria-label="Lọc điểm thực hiện CLS"
          className="h-9 rounded-lg border border-border bg-canvas px-2.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
        >
          <option value="ALL">Tất cả quầy / phòng CLS</option>
          {servicePoints.map((sp) => (
            <option key={sp.formattedServicePointId} value={sp.formattedServicePointId}>
              {sp.roomCode} • {sp.servicePointName}
            </option>
          ))}
        </select>

        <div className="flex items-center gap-2">
          <select
            value={filters.requiredForConclusion}
            onChange={(e) =>
              onFilterChange({
                requiredForConclusion: e.target
                  .value as DiagnosticWorklistFilterFormValues['requiredForConclusion'],
                page: 1,
              })
            }
            aria-label="Lọc điều kiện kết luận"
            className="h-9 min-w-0 flex-1 rounded-lg border border-border bg-canvas px-2 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
          >
            <option value="ALL">Tất cả chỉ định</option>
            <option value="REQUIRED">Bắt buộc cho kết luận</option>
            <option value="OPTIONAL">Tùy chọn bổ trợ</option>
          </select>

          <input
            type="date"
            value={filters.queueDate}
            onChange={(e) => onFilterChange({ queueDate: e.target.value, page: 1 })}
            aria-label="Ngày chỉ định CLS"
            className="h-9 w-34 rounded-lg border border-border bg-canvas px-2 text-xs font-mono text-ink focus:border-primary focus:bg-white focus:outline-none"
          />
        </div>
      </div>

      {validationError && <Alert tone="warning">{validationError}</Alert>}
    </div>
  );
}
