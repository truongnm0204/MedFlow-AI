import { Barcode, Search, X } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import type {
  PrescriptionSearchFilters,
  PrescriptionSearchStatus,
} from '../types/prescription-search.types';

interface PrescriptionSearchFiltersBarProps {
  filters: PrescriptionSearchFilters;
  onChange: (patch: Partial<PrescriptionSearchFilters>) => void;
  doctors: string[];
  counts: {
    all: number;
    pendingApproval: number;
    preparing: number;
    readyForDispense: number;
  };
  onScanBarcode?: () => void;
}

export function PrescriptionSearchFiltersBar({
  filters,
  onChange,
  doctors,
  counts,
  onScanBarcode,
}: PrescriptionSearchFiltersBarProps) {
  const tabs: Array<{ id: 'ALL' | PrescriptionSearchStatus; label: string; count: number }> = [
    { id: 'ALL', label: 'Tất cả', count: counts.all },
    {
      id: 'PENDING_APPROVAL',
      label: 'Mới phát hành - Chờ tiếp nhận',
      count: counts.pendingApproval,
    },
    { id: 'PREPARING', label: 'Đang chuẩn bị', count: counts.preparing },
    { id: 'READY_FOR_DISPENSE', label: 'Chờ phát thuốc', count: counts.readyForDispense },
  ];

  return (
    <div className="space-y-4 rounded-2xl border border-border bg-white p-4 shadow-2xs">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
        {/* Search Input */}
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
            aria-hidden="true"
          />
          <input
            type="text"
            value={filters.query}
            onChange={(e) => onChange({ query: e.target.value })}
            placeholder="Tìm theo mã đơn thuốc (RX-xxxx), mã BN, tên bệnh nhân, số điện thoại..."
            className="h-10 w-full rounded-xl border border-border bg-canvas pl-10 pr-10 text-xs text-ink placeholder:text-sub focus:bg-white focus:outline-hidden focus:ring-2 focus:ring-primary"
          />
          {filters.query && (
            <button
              type="button"
              onClick={() => onChange({ query: '' })}
              aria-label="Xóa từ khóa tìm kiếm"
              className="absolute right-3 top-1/2 -translate-y-1/2 rounded p-1 text-sub hover:bg-slate-200 hover:text-ink"
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          )}
        </div>

        {/* Doctor filter */}
        <select
          value={filters.doctor}
          onChange={(e) => onChange({ doctor: e.target.value })}
          aria-label="Lọc theo bác sĩ kê đơn"
          className="h-10 rounded-xl border border-border bg-canvas px-3 text-xs text-ink focus:bg-white focus:outline-hidden focus:ring-2 focus:ring-primary"
        >
          <option value="">Tất cả bác sĩ</option>
          {doctors.map((d) => (
            <option key={d} value={d}>
              {d}
            </option>
          ))}
        </select>

        {/* Barcode scan button */}
        <Button
          type="button"
          variant="primary"
          size="sm"
          onClick={onScanBarcode}
          className="h-10 gap-2 shrink-0 shadow-xs"
        >
          <Barcode className="h-4 w-4" aria-hidden="true" />
          <span>Quét mã vạch [F2]</span>
        </Button>
      </div>

      {/* Status filter pills */}
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="Trạng thái đơn thuốc">
        {tabs.map((tab) => {
          const active = filters.status === tab.id;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => onChange({ status: tab.id })}
              className={`inline-flex items-center gap-2 rounded-xl px-3.5 py-1.5 text-xs font-semibold transition-colors ${
                active
                  ? 'bg-primary text-white shadow-2xs'
                  : 'border border-border bg-canvas text-sub hover:bg-slate-100 hover:text-ink'
              }`}
            >
              <span>{tab.label}</span>
              <span
                className={`rounded-full px-1.5 py-0.5 font-mono text-[10px] font-bold ${
                  active ? 'bg-white/20 text-white' : 'bg-white text-navy'
                }`}
              >
                {tab.count}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
