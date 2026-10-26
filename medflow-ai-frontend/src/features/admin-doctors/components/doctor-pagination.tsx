import { Button } from '@/shared/ui/button';

interface DoctorPaginationProps {
  count: number;
  currentPage: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

export function DoctorPagination({
  count,
  currentPage,
  pageSize,
  totalPages,
  onPageChange,
}: DoctorPaginationProps) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 p-4 text-xs text-sub">
      <span>
        Hiển thị{' '}
        <b>
          {count ? (currentPage - 1) * pageSize + 1 : 0} - {Math.min(currentPage * pageSize, count)}
        </b>{' '}
        trong tổng số <b>{count}</b> Bác sĩ{' '}
        <span className="ml-4">
          Số dòng: <b>{pageSize} dòng / trang</b>
        </span>
      </span>
      <div className="flex items-center gap-1">
        <Button
          variant="secondary"
          size="sm"
          disabled={currentPage === 1}
          onClick={() => onPageChange(currentPage - 1)}
        >
          ‹ Trước
        </Button>
        {Array.from({ length: totalPages }, (_, index) => (
          <button
            key={index}
            type="button"
            onClick={() => onPageChange(index + 1)}
            className={`min-w-8 rounded border px-2 py-1.5 ${currentPage === index + 1 ? 'border-primary bg-primary text-white' : 'border-border'}`}
          >
            {index + 1}
          </button>
        ))}
        <Button
          variant="secondary"
          size="sm"
          disabled={currentPage === totalPages}
          onClick={() => onPageChange(currentPage + 1)}
        >
          Tiếp ›
        </Button>
      </div>
    </div>
  );
}
