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
        <div className="space-y-4">
          <div className="h-6 w-48 animate-pulse rounded bg-slate-200" />
          <div className="divide-y divide-slate-100">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="flex items-center justify-between py-4">
                <div className="space-y-2">
                  <div className="h-4 w-32 animate-pulse rounded bg-slate-200" />
                  <div className="h-3 w-64 animate-pulse rounded bg-slate-100" />
                </div>
                <div className="h-4 w-20 animate-pulse rounded bg-slate-200" />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center rounded-xl border border-border bg-white px-6 py-16 text-center shadow-xs">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-50 text-slate-400">
          <Inbox className="h-7 w-7" aria-hidden="true" />
        </div>
        <h3 className="mt-4 text-sm font-bold text-ink">Không tìm thấy dịch vụ nào phù hợp</h3>
        <p className="mt-1 max-w-sm text-xs text-sub">
          Không có kết quả nào khớp với các tiêu chí tìm kiếm hoặc bộ lọc hiện tại. Thử nới lỏng
          hoặc đặt lại bộ lọc.
        </p>
        <button
          type="button"
          onClick={onResetFilters}
          className="mt-4 inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50"
        >
          Xóa toàn bộ bộ lọc
        </button>
      </div>
    );
  }

  const startRecord = (page - 1) * pageSize + 1;
  const endRecord = Math.min(page * pageSize, total);

  return (
    <div className="rounded-xl border border-border bg-white shadow-xs overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead className="border-b border-border bg-slate-50/75 text-[11px] font-bold uppercase tracking-wider text-sub">
            <tr>
              <th scope="col" className="px-4 py-3 font-semibold">
                Mã DV
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Tên Dịch Vụ & Mô Tả
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Khoa Phụ Trách
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Phân Loại
              </th>
              <th scope="col" className="px-4 py-3 font-semibold text-right">
                Đơn Giá Niêm Yết
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Quyết Định & Hiệu Lực
              </th>
              <th scope="col" className="px-4 py-3 font-semibold text-center">
                Trạng Thái
              </th>
              <th scope="col" className="px-4 py-3 font-semibold text-center">
                Thao Tác
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {items.map((item) => (
              <tr key={item.id} className="transition-colors hover:bg-slate-50/70">
                {/* Code */}
                <td className="whitespace-nowrap px-4 py-3.5 align-top">
                  <span className="inline-block rounded border border-sky-100 bg-sky-50 px-2 py-0.5 font-mono text-[11px] font-bold text-primary">
                    {item.code}
                  </span>
                </td>

                {/* Name & Description */}
                <td className="px-4 py-3.5 align-top max-w-sm">
                  <div className="font-bold text-ink text-xs leading-snug">{item.name}</div>
                  <div className="mt-0.5 line-clamp-2 text-[11px] text-sub leading-relaxed">
                    {item.description}
                  </div>
                  {item.onlineBooking && (
                    <div className="mt-1.5 flex items-center gap-1.5 text-[10px] text-emerald-700 font-medium">
                      <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                      Hỗ trợ đặt lịch hẹn trực tuyến
                    </div>
                  )}
                </td>

                {/* Department */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap text-slate-700">
                  <div className="font-medium text-xs">{item.department}</div>
                  {item.roomDefault && (
                    <div className="mt-0.5 text-[10px] text-sub">{item.roomDefault}</div>
                  )}
                </td>

                {/* Category */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap">
                  <span className="inline-block rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-700 border border-slate-200">
                    {item.category}
                  </span>
                </td>

                {/* Price */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap text-right">
                  <div className="font-bold text-sm text-emerald-700">{item.formattedPrice}</div>
                  <div className="mt-0.5 text-[10px] text-sub">Giá thu ngoại trú</div>
                </td>

                {/* Decision Number & Date */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap text-slate-600">
                  <div className="font-mono text-[11px] font-semibold text-ink">
                    {item.decisionNumber}
                  </div>
                  <div className="mt-0.5 flex items-center gap-1 text-[10px] text-sub">
                    <Clock className="h-3 w-3 text-slate-400" aria-hidden="true" />
                    <span>Áp dụng từ: {item.effectiveDate}</span>
                  </div>
                </td>

                {/* Status Badge */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap text-center">
                  {item.status === 'ACTIVE' ? (
                    <span className="inline-flex items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-[10px] font-bold text-emerald-700">
                      <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                      Đang áp dụng
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-0.5 text-[10px] font-bold text-amber-700">
                      <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
                      Tạm dừng
                    </span>
                  )}
                </td>

                {/* Action Buttons */}
                <td className="px-4 py-3.5 align-top whitespace-nowrap text-center">
                  <div className="flex items-center justify-center gap-1.5">
                    <button
                      type="button"
                      onClick={() => onUpdatePrice(item)}
                      className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50/70 px-2.5 py-1 text-[11px] font-semibold text-emerald-700 transition-colors hover:border-emerald-300 hover:bg-emerald-100"
                      aria-label={`Cập nhật giá của ${item.name}`}
                    >
                      <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Cập nhật giá</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => onViewHistory(item)}
                      className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-primary transition-colors hover:border-sky-300 hover:bg-sky-50/70"
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

      {/* Pagination Footer */}
      <div className="flex flex-col items-center justify-between gap-3 border-t border-border bg-slate-50/60 px-4 py-3 sm:flex-row">
        <div className="flex items-center gap-3 text-xs text-sub">
          <span>
            Hiển thị <strong className="text-ink">{startRecord}</strong> -{' '}
            <strong className="text-ink">{endRecord}</strong> trên tổng số{' '}
            <strong className="text-ink">{total}</strong> dịch vụ
          </span>
          <div className="flex items-center gap-1.5">
            <label htmlFor="pricing-page-size" className="text-[11px]">
              Mỗi trang:
            </label>
            <select
              id="pricing-page-size"
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              className="h-7 rounded border border-border bg-white px-1.5 text-xs text-ink focus:border-primary focus:outline-none"
            >
              <option value={10}>10</option>
              <option value={20}>20</option>
              <option value={50}>50</option>
            </select>
          </div>
        </div>

        <div className="flex items-center gap-1">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Trang trước"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-border bg-white text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="px-2 text-xs font-semibold text-ink">
            Trang {page} / {totalPages}
          </span>
          <button
            type="button"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Trang tiếp theo"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-border bg-white text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
