import { ChevronLeft, ChevronRight } from 'lucide-react';

import { Button } from '@/shared/ui/button';

interface SlotPaginationProps {
  count: number;
  currentPage: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
}

export function SlotPagination({
  count,
  currentPage,
  pageSize,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: SlotPaginationProps) {
  const startItem = count > 0 ? (currentPage - 1) * pageSize + 1 : 0;
  const endItem = Math.min(currentPage * pageSize, count);

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border bg-white px-6 py-3.5 text-xs text-sub">
      {/* Counts & Rows selector */}
      <div className="flex flex-wrap items-center gap-4">
        <span>
          Hiển thị <b>{startItem}</b> - <b>{endItem}</b> trong tổng số <b>{count}</b> Slot khám
        </span>

        <div className="flex items-center gap-2">
          <span>Số dòng:</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className="rounded-md border border-border bg-white px-2 py-1 text-xs text-ink shadow-2xs outline-none focus:border-primary"
            aria-label="Số dòng trên mỗi trang"
          >
            <option value={8}>8 dòng / trang</option>
            <option value={10}>10 dòng / trang</option>
            <option value={20}>20 dòng / trang</option>
            <option value={50}>50 dòng / trang</option>
          </select>
        </div>
      </div>

      {/* Pagination Controls */}
      <div className="flex items-center gap-1.5">
        <Button
          variant="secondary"
          size="sm"
          disabled={currentPage <= 1}
          onClick={() => onPageChange(currentPage - 1)}
          className="gap-1 px-2.5"
          aria-label="Trang trước"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Trước</span>
        </Button>

        <div className="flex items-center gap-1">
          {Array.from({ length: totalPages }, (_, index) => {
            const pageNumber = index + 1;
            const isActive = currentPage === pageNumber;

            // Simple pagination window logic: show first, last, and around current
            if (
              totalPages > 7 &&
              pageNumber !== 1 &&
              pageNumber !== totalPages &&
              Math.abs(pageNumber - currentPage) > 1
            ) {
              if (pageNumber === 2 || pageNumber === totalPages - 1) {
                return (
                  <span key={pageNumber} className="px-1 text-slate-400 font-mono">
                    ...
                  </span>
                );
              }
              return null;
            }

            return (
              <button
                key={pageNumber}
                type="button"
                onClick={() => onPageChange(pageNumber)}
                className={`min-w-8 h-8 rounded border text-xs font-semibold transition-colors ${
                  isActive
                    ? 'border-primary bg-primary text-white shadow-2xs'
                    : 'border-border bg-white text-slate-700 hover:bg-slate-50'
                }`}
                aria-current={isActive ? 'page' : undefined}
                aria-label={`Trang ${pageNumber}`}
              >
                {pageNumber}
              </button>
            );
          })}
        </div>

        <Button
          variant="secondary"
          size="sm"
          disabled={currentPage >= totalPages}
          onClick={() => onPageChange(currentPage + 1)}
          className="gap-1 px-2.5"
          aria-label="Trang tiếp"
        >
          <span>Tiếp</span>
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}
