import { ChevronDown, RotateCcw } from 'lucide-react';

interface ServiceFilterBarProps {
  selectedDepartment: string;
  selectedPriceDuration: string;
  onSelectDepartment: (dept: string) => void;
  onSelectPriceDuration: (pd: string) => void;
  onResetFilters: () => void;
  hasActiveFilters: boolean;
}

const DEPARTMENTS = [
  { id: 'all', label: 'Tất cả khoa phòng' },
  { id: 'dept-th', label: 'Khoa Tiêu Hóa - Gan Mật' },
  { id: 'dept-tm', label: 'Khoa Tim Mạch' },
  { id: 'dept-noi', label: 'Khoa Nội Tổng Quát' },
  { id: 'dept-tmh', label: 'Khoa Tai Mũi Họng' },
  { id: 'dept-nhi', label: 'Khoa Nhi Ngoại Trú' },
  { id: 'dept-cxk', label: 'Khoa Cơ Xương Khớp' },
  { id: 'dept-tk', label: 'Khoa Nội Thần Kinh' },
  { id: 'dept-nt', label: 'Khoa Nội Tiết' },
  { id: 'dept-ts', label: 'Trung tâm Khám Sức Khỏe' },
];

const PRICE_DURATION_OPTIONS = [
  { id: 'all', label: 'Mức giá / Thời lượng khám' },
  { id: 'under-200k', label: 'Dưới 200.000 VNĐ' },
  { id: '200k-250k', label: '200.000 - 250.000 VNĐ' },
  { id: 'over-250k', label: 'Trên 250.000 VNĐ' },
  { id: 'duration-under-20', label: 'Thời lượng: Dưới 20 phút' },
  { id: 'duration-over-20', label: 'Thời lượng: Từ 20 phút trở lên' },
];

export function ServiceFilterBar({
  selectedDepartment,
  selectedPriceDuration,
  onSelectDepartment,
  onSelectPriceDuration,
  onResetFilters,
  hasActiveFilters,
}: ServiceFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 pt-2">
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative">
          <select
            aria-label="Lọc theo khoa phòng"
            value={selectedDepartment}
            onChange={(e) => onSelectDepartment(e.target.value)}
            className="cursor-pointer appearance-none rounded-lg border border-border bg-white py-2 pr-8 pl-3.5 text-xs font-medium text-ink shadow-sm transition-all hover:border-slate-300 focus:border-transparent focus:ring-2 focus:ring-primary focus:outline-none"
          >
            {DEPARTMENTS.map((dept) => (
              <option key={dept.id} value={dept.id}>
                {dept.label}
              </option>
            ))}
          </select>
          <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-sub">
            <ChevronDown className="h-4 w-4" aria-hidden="true" />
          </div>
        </div>

        <div className="relative">
          <select
            aria-label="Lọc theo mức giá hoặc thời lượng khám"
            value={selectedPriceDuration}
            onChange={(e) => onSelectPriceDuration(e.target.value)}
            className="cursor-pointer appearance-none rounded-lg border border-border bg-white py-2 pr-8 pl-3.5 text-xs font-medium text-ink shadow-sm transition-all hover:border-slate-300 focus:border-transparent focus:ring-2 focus:ring-primary focus:outline-none"
          >
            {PRICE_DURATION_OPTIONS.map((opt) => (
              <option key={opt.id} value={opt.id}>
                {opt.label}
              </option>
            ))}
          </select>
          <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-sub">
            <ChevronDown className="h-4 w-4" aria-hidden="true" />
          </div>
        </div>
      </div>

      {hasActiveFilters && (
        <button
          type="button"
          onClick={onResetFilters}
          className="ml-auto flex items-center gap-1.5 text-xs font-medium text-sub transition-colors hover:text-rose-600 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
        >
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Xóa tất cả bộ lọc</span>
        </button>
      )}
    </div>
  );
}
