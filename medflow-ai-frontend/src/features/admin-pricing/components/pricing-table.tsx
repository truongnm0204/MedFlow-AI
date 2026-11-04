import { ChevronLeft, ChevronRight, Clock, History, Inbox, Pencil } from 'lucide-react';

import type { ServicePricingItem } from '../types/service-pricing.types';

interface PricingTableProps {
  items: ServicePricingItem[];
  isLoading: boolean;
  page: number;
  pageSize: number;
  totalPages: number;
  total: number;
  onPageChange: (newPage: number) => void;
  onPageSizeChange: (newPageSize: number) => void;
  onViewHistory: (item: ServicePricingItem) => void;
  onUpdatePrice: (item: ServicePricingItem) => void;
  onResetFilters: () => void;
}

export function PricingTable({
  items,
  isLoading,
  page,
  pageSize,
  totalPages,
  total,
  onPageChange,
  onPageSizeChange,
  onViewHistory,
  onUpdatePrice,
  onResetFilters,
}: PricingTableProps) {
  if (isLoading) {
    return (
      <div className="rounded-xl border border-border bg-white p-6 shadow-xs">
        <div className="space-y-3">
          <div className="h-5 w-40 animate-pulse rounded bg-slate-200" />
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="h-10 animate-pulse rounded bg-slate-100" />
          ))}
        </div>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center rounded-xl border border-border bg-white p-12 text-center shadow-xs">
        <Inbox className="h-8 w-8 text-slate-300" aria-hidden="true" />
        <h3 className="mt-2 text-sm font-bold text-ink">Không tìm thấy dịch vụ nào phù hợp</h3>
        <p className="mt-1 text-xs text-sub">Thử điều chỉnh hoặc xóa bớt tiêu chí lọc hiện tại.</p>
        <button
          type="button"
          onClick={onResetFilters}
          className="mt-3 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
        >
          Xóa toàn bộ bộ lọc
        </button>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-border bg-white shadow-xs overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead className="border-b border-border bg-slate-50 text-[11px] font-bold uppercase text-sub">
            <tr>
              <th className="px-4 py-3">Mã DV</th>
              <th className="px-4 py-3">Tên Dịch Vụ</th>
              <th className="px-4 py-3">Khoa Phụ Trách</th>
              <th className="px-4 py-3">Phân Loại</th>
              <th className="px-4 py-3 text-right">Đơn Giá Niêm Yết</th>
              <th className="px-4 py-3">Số Quyết Định</th>
              <th className="px-4 py-3 text-center">Trạng Thái</th>
              <th className="px-4 py-3 text-center">Thao Tác</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {items.map((item) => (
              <tr key={item.id} className="hover:bg-slate-50/70 transition-colors">
                <td className="px-4 py-3 whitespace-nowrap">
                  <span className="font-mono text-[11px] font-bold text-primary bg-sky-50 border border-sky-100 px-2 py-0.5 rounded">
                    {item.code}
                  </span>
                </td>
                <td className="px-4 py-3 max-w-xs">
                  <div className="font-bold text-ink text-xs">{item.name}</div>
                  <div className="line-clamp-1 text-[11px] text-sub">{item.description}</div>
                </td>
                <td className="px-4 py-3 whitespace-nowrap text-slate-700">{item.department}</td>
                <td className="px-4 py-3 whitespace-nowrap">
                  <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-600 border border-slate-200">
                    {item.category}
                  </span>
                </td>
                <td className="px-4 py-3 whitespace-nowrap text-right font-bold text-sm text-emerald-700">
                  {item.formattedPrice}
                </td>
                <td className="px-4 py-3 whitespace-nowrap text-slate-600">
                  <div className="font-mono text-[11px] font-semibold">{item.decisionNumber}</div>
                  <div className="flex items-center gap-1 text-[10px] text-sub">
                    <Clock className="h-3 w-3 text-slate-400" />
                    <span>Từ: {item.effectiveDate}</span>
                  </div>
                </td>
                <td className="px-4 py-3 whitespace-nowrap text-center">
                  {item.status === 'ACTIVE' ? (
                    <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[10px] font-bold text-emerald-700">
                      <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" /> Đang áp dụng
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[10px] font-bold text-amber-700">
                      <span className="h-1.5 w-1.5 rounded-full bg-amber-500" /> Tạm dừng
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 whitespace-nowrap text-center">
                  <div className="flex items-center justify-center gap-1.5">
                    <button
                      type="button"
                      onClick={() => onUpdatePrice(item)}
                      className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50/70 px-2 py-1 text-[11px] font-semibold text-emerald-700 hover:bg-emerald-100"
                      aria-label={`Cập nhật giá của ${item.name}`}
                    >
                      <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Cập nhật giá</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => onViewHistory(item)}
                      className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2 py-1 text-[11px] font-semibold text-primary hover:bg-sky-50"
                      aria-label={`Xem lịch sử giá của ${item.name}`}
                    >
                      <History className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Lịch sử giá</span>
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between border-t border-border bg-slate-50/60 px-4 py-2.5 text-xs text-sub">
        <div className="flex items-center gap-2">
          <span>
            Tổng số: <strong className="text-ink">{total}</strong> dịch vụ
          </span>
          <select
            aria-label="Mỗi trang"
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className="h-6 rounded border border-border bg-white px-1 text-xs"
          >
            <option value={10}>10</option>
            <option value={20}>20</option>
          </select>
        </div>
        <div className="flex items-center gap-1">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Trang trước"
            className="rounded p-1 text-slate-500 hover:bg-slate-100 disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span>
            Trang {page} / {totalPages}
          </span>
          <button
            type="button"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Trang tiếp theo"
            className="rounded p-1 text-slate-500 hover:bg-slate-100 disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
