import { RefreshCw } from 'lucide-react';

import { Alert } from '@/shared/ui';

import type { Hospital } from '../types/booking-entry.types';
import { BookingHospitalCard } from './booking-hospital-card';

interface BookingHospitalListProps {
  hospitals: Hospital[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  selectedHospitalId: string | null;
  onSelect: (id: string) => void;
}

function SkeletonCard() {
  return (
    <div className="animate-pulse rounded-2xl border border-border bg-white p-4">
      <div className="flex items-start gap-3">
        <div className="h-10 w-10 rounded-xl bg-slate-100" />
        <div className="flex-1 space-y-2">
          <div className="h-3.5 w-3/4 rounded bg-slate-100" />
          <div className="h-3 w-full rounded bg-slate-100" />
          <div className="h-3 w-1/2 rounded bg-slate-100" />
        </div>
      </div>
    </div>
  );
}

export function BookingHospitalList({
  hospitals,
  isLoading,
  isError,
  onRetry,
  selectedHospitalId,
  onSelect,
}: BookingHospitalListProps) {
  if (isLoading) {
    return (
      <div
        role="status"
        aria-label="Đang tải danh sách cơ sở khám"
        className="grid grid-cols-1 gap-3 md:grid-cols-2"
      >
        <SkeletonCard />
        <SkeletonCard />
      </div>
    );
  }

  if (isError) {
    return (
      <Alert tone="error" className="flex items-center justify-between gap-4">
        <span>Không thể tải danh sách cơ sở khám. Vui lòng kiểm tra kết nối và thử lại.</span>
        <button
          type="button"
          onClick={onRetry}
          className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 hover:bg-rose-50"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Thử lại
        </button>
      </Alert>
    );
  }

  if (hospitals.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-border bg-white p-6 text-center">
        <p className="text-sm font-semibold text-ink">
          Hiện chưa có cơ sở nào tiếp nhận đặt lịch trực tuyến
        </p>
        <p className="mt-1 text-xs text-sub">
          Vui lòng gọi tổng đài <strong className="font-semibold text-ink">1900 6868</strong> để
          được hỗ trợ.
        </p>
      </div>
    );
  }

  return (
    <div
      role="radiogroup"
      aria-label="Danh sách cơ sở khám"
      className="grid grid-cols-1 gap-3 md:grid-cols-2"
    >
      {hospitals.map((hospital) => (
        <BookingHospitalCard
          key={hospital.id}
          hospital={hospital}
          selected={hospital.id === selectedHospitalId}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}
