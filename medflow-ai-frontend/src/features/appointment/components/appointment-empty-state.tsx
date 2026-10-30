import { Link } from 'react-router-dom';
import { CalendarPlus, RotateCcw, SearchX } from 'lucide-react';

interface AppointmentEmptyStateProps {
  hasActiveFilters: boolean;
  onResetFilters: () => void;
}

export function AppointmentEmptyState({
  hasActiveFilters,
  onResetFilters,
}: AppointmentEmptyStateProps) {
  return (
    <div className="rounded-2xl border border-dashed border-border bg-white p-10 text-center shadow-2xs">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-sky-50 text-primary">
        <SearchX className="h-6 w-6" aria-hidden="true" />
      </div>

      <h3 className="mt-4 text-base font-bold text-ink">
        {hasActiveFilters
          ? 'Không tìm thấy lịch hẹn phù hợp với bộ lọc'
          : 'Chưa có lịch hẹn khám ngoại trú nào'}
      </h3>

      <p className="mx-auto mt-1.5 max-w-md text-xs leading-relaxed text-sub">
        {hasActiveFilters
          ? 'Hồ sơ thành viên hoặc điều kiện tra cứu hiện tại chưa có lịch hẹn nào khớp. Bạn có thể đặt lại bộ lọc hoặc tạo lịch khám mới.'
          : 'Bạn có thể chủ động đặt lịch khám chuyên khoa ngoại trú cho bản thân hoặc người thân trong gia đình để được giữ số thứ tự trước.'}
      </p>

      <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
        {hasActiveFilters && (
          <button
            type="button"
            onClick={onResetFilters}
            className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Xóa bộ lọc</span>
          </button>
        )}

        <Link
          to="/appointment/services"
          className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
        >
          <CalendarPlus className="h-3.5 w-3.5" aria-hidden="true" />
          <span>+ Đặt lịch khám mới</span>
        </Link>
      </div>
    </div>
  );
}
