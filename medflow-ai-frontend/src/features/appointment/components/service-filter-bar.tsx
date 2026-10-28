import { RotateCcw } from 'lucide-react';

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
            value={selectedDepartment}
            onChange={(e) => onSelectDepartment(e.target.value)}
            className="appearance-none bg-white border border-slate-200 rounded-lg px-3.5 py-2 pr-8 text-xs font-medium text-slate-700 hover:border-slate-300 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-transparent transition-all shadow-sm cursor-pointer"
          >
            {DEPARTMENTS.map((dept) => (
              <option key={dept.id} value={dept.id}>
                {dept.label}
              </option>
            ))}
          </select>
          <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-slate-400">
            <svg className="w-4 h-4 fill-current" viewBox="0 0 20 20">
              <path d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" />
            </svg>
          </div>
        </div>

        <div className="relative">
          <select
            value={selectedPriceDuration}
            onChange={(e) => onSelectPriceDuration(e.target.value)}
            className="appearance-none bg-white border border-slate-200 rounded-lg px-3.5 py-2 pr-8 text-xs font-medium text-slate-700 hover:border-slate-300 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-transparent transition-all shadow-sm cursor-pointer"
          >
            {PRICE_DURATION_OPTIONS.map((opt) => (
              <option key={opt.id} value={opt.id}>
                {opt.label}
              </option>
            ))}
          </select>
          <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-slate-400">
            <svg className="w-4 h-4 fill-current" viewBox="0 0 20 20">
              <path d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" />
            </svg>
          </div>
        </div>
      </div>

      {hasActiveFilters && (
        <button
          type="button"
          onClick={onResetFilters}
          className="flex items-center gap-1.5 text-xs font-medium text-slate-500 hover:text-rose-600 transition-colors ml-auto"
        >
          <RotateCcw className="w-3.5 h-3.5" />
          <span>Xóa tất cả bộ lọc</span>
        </button>
      )}
    </div>
  );
}

