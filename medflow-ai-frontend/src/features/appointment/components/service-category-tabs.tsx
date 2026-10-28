import { SlidersHorizontal } from 'lucide-react';
import type { ServiceCategory } from '../types/service-search.types';

interface ServiceCategoryTabsProps {
  selectedCategory: ServiceCategory;
  onSelectCategory: (category: ServiceCategory) => void;
}

const CATEGORIES: { id: ServiceCategory; label: string }[] = [
  { id: 'all', label: 'Tất cả dịch vụ' },
  { id: 'specialty', label: 'Khám Chuyên khoa Đơn lẻ' },
  { id: 'package', label: 'Gói Tầm soát Toàn diện' },
  { id: 'pediatric', label: 'Khám Nhi Tự Nguyện' },
  { id: 'general', label: 'Khám Nội Tổng Quát' },
];

export function ServiceCategoryTabs({
  selectedCategory,
  onSelectCategory,
}: ServiceCategoryTabsProps) {
  return (
    <div className="flex flex-wrap items-center gap-2 pt-2">
      <div className="flex items-center gap-1.5 text-xs font-semibold text-slate-500 uppercase tracking-wider mr-1">
        <SlidersHorizontal className="w-3.5 h-3.5" />
        <span>Phân loại:</span>
      </div>
      {CATEGORIES.map((cat) => {
        const isActive = selectedCategory === cat.id;
        return (
          <button
            key={cat.id}
            type="button"
            onClick={() => onSelectCategory(cat.id)}
            className={`px-3.5 py-1.5 rounded-lg text-xs font-medium transition-all ${
              isActive
                ? 'bg-sky-600 text-white shadow-sm'
                : 'bg-slate-100 text-slate-700 hover:bg-slate-200'
            }`}
          >
            {cat.label}
          </button>
        );
      })}
    </div>
  );
}

