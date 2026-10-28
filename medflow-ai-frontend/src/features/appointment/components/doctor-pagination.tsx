import { ChevronLeft, ChevronRight } from 'lucide-react';

interface DoctorPaginationProps {
  currentCount: number;
  total: number;
  page: number;
  totalPages: number;
  onPageChange: (newPage: number) => void;
}

export function DoctorPagination({
  currentCount,
  total,
  page,
  totalPages,
  onPageChange,
}: DoctorPaginationProps) {
  const pages = Array.from({ length: totalPages }, (_, i) => i + 1);

  return (
    <div className="flex flex-col items-center justify-between gap-4 border-t border-slate-100 pt-5 sm:flex-row">
      <div className="text-xs text-slate-500">
        Hiển thị <span className="font-bold text-slate-800">{currentCount}</span> /{' '}
        <span className="font-bold text-slate-800">{total}</span> bác sĩ chuyên khoa sẵn sàng nhận
        lịch khám
      </div>

      <div className="flex items-center gap-1.5">
        <button
          type="button"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
          className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 shadow-sm transition-all hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
          aria-label="Trang trước"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>

        {pages.map((p) => {
          const isActive = p === page;
          return (
            <button
              key={p}
              type="button"
              onClick={() => onPageChange(p)}
              className={`flex h-8 w-8 items-center justify-center rounded-lg text-xs font-semibold shadow-sm transition-all ${
                isActive
                  ? 'bg-sky-600 text-white'
                  : 'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              {p}
            </button>
          );
        })}

        <button
          type="button"
          disabled={page >= totalPages}
          onClick={() => onPageChange(page + 1)}
          className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 shadow-sm transition-all hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
          aria-label="Trang tiếp"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}
