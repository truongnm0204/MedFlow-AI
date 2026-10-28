import { ChevronLeft, ChevronRight } from 'lucide-react';

interface ServicePaginationProps {
  currentPage: number;
  totalPages: number;
  totalItems: number;
  currentCount: number;
  onPageChange: (page: number) => void;
}

export function ServicePagination({
  currentPage,
  totalPages,
  totalItems,
  currentCount,
  onPageChange,
}: ServicePaginationProps) {
  if (totalItems === 0) return null;

  return (
    <div className="flex flex-col sm:flex-row items-center justify-between gap-4 pt-4 border-t border-slate-100">
      <div className="text-xs text-slate-500">
        Hiển thị <span className="font-semibold text-slate-800">{currentCount}</span> /{' '}
        <span className="font-semibold text-slate-800">{totalItems}</span> dịch vụ khám ngoại trú
      </div>

      {totalPages > 1 && (
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            disabled={currentPage <= 1}
            onClick={() => onPageChange(currentPage - 1)}
            aria-label="Trang trước"
            className="w-8 h-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-xs"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>

          {Array.from({ length: totalPages }, (_, i) => i + 1).map((pageNum) => {
            const isActive = pageNum === currentPage;
            return (
              <button
                key={pageNum}
                type="button"
                onClick={() => onPageChange(pageNum)}
                className={`w-8 h-8 rounded-lg text-xs font-medium transition-colors ${
                  isActive
                    ? 'bg-sky-600 text-white font-semibold shadow-sm'
                    : 'bg-white border border-slate-200 text-slate-700 hover:bg-slate-50'
                }`}
              >
                {pageNum}
              </button>
            );
          })}

          <button
            type="button"
            disabled={currentPage >= totalPages}
            onClick={() => onPageChange(currentPage + 1)}
            aria-label="Trang sau"
            className="px-2.5 h-8 flex items-center gap-1 rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors text-xs font-medium"
          >
            <span>Tiếp</span>
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      )}
    </div>
  );
}

