import { ChevronLeft, ChevronRight } from 'lucide-react';

import { Button } from '@/shared/ui/button';

interface ServicePaginationProps {
  count: number;
  currentPage: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
}

export function ServicePagination({
  count,
  currentPage,
  pageSize,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: ServicePaginationProps) {
  const startItem = count > 0 ? (currentPage - 1) * pageSize + 1 : 0;
  const endItem = Math.min(currentPage * pageSize, count);

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border bg-white px-6 py-3.5 text-xs text-sub">
      <div className="flex flex-wrap items-center gap-4">
        <span>
          Hiển thị <b>{startItem}</b> - <b>{endItem}</b> trong tổng số <b>{count}</b> Dịch vụ
        </span>

        <div className="flex items-center gap-2">
          <span>Xem:</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className="rounded border border-border bg-white px-2 py-1 text-xs text-ink shadow-xs outline-none focus:border-primary"
            aria-label="Số dòng trên mỗi trang"
          >
            <option value={8}>8 dòng / trang</option>
            <option value={10}>10 dòng / trang</option>
            <option value={20}>20 dòng / trang</option>
            <option value={50}>50 dòng / trang</option>
          </select>
        </div>
      </div>

      <div className="flex items-center gap-1.5">
        <Button
          variant="secondary"
          size="sm"
          disabled={currentPage === 1}
          onClick={() => onPageChange(currentPage - 1)}
          className="gap-1 px-2.5"
          aria-label="Trang trước"
        >
          <ChevronLeft className="h-3.5 w-3.5" />
          <span>Trước</span>
        </Button>

        <div className="flex items-center gap-1">
          {Array.from({ length: totalPages }, (_, index) => {
            const pageNumber = index + 1;
            const isActive = currentPage === pageNumber;
            return (
              <button
                key={pageNumber}
                type="button"
                onClick={() => onPageChange(pageNumber)}
                className={`min-w-8 h-8 rounded border text-xs font-medium transition-colors ${
                  isActive
                    ? 'border-primary bg-primary text-white shadow-xs'
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
          disabled={currentPage === totalPages}
          onClick={() => onPageChange(currentPage + 1)}
          className="gap-1 px-2.5"
          aria-label="Trang tiếp"
        >
          <span>Tiếp</span>
          <ChevronRight className="h-3.5 w-3.5" />
        </Button>
      </div>
    </div>
  );
}
