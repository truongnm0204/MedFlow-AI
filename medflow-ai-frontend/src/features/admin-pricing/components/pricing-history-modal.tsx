import { AlertCircle, Calendar, TrendingDown, TrendingUp, X } from 'lucide-react';
import { useEffect } from 'react';

import type { ServicePricingItem } from '../types/service-pricing.types';

interface PricingHistoryModalProps {
  item: ServicePricingItem | null;
  onClose: () => void;
}

export function PricingHistoryModal({ item, onClose }: PricingHistoryModalProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  if (!item) return null;

  const formatVnd = (num: number) => `${num.toLocaleString('vi-VN')} đ`;

  const history = item.priceHistory ?? [
    {
      date: item.effectiveDate,
      price: item.currentPrice,
      decisionNumber: item.decisionNumber,
      note: 'Biểu phí hiện hành áp dụng năm 2026',
      appliedBy: 'Hội đồng Giá Bệnh viện',
    },
  ];

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="pricing-history-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-2xl rounded-xl border border-border bg-white shadow-xl animate-in fade-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <span className="inline-block rounded border border-sky-100 bg-sky-50 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">
              {item.code}
            </span>
            <h2 id="pricing-history-title" className="mt-1 text-sm font-bold text-ink">
              Lịch Sử Biểu Phí & Quyết Định Áp Dụng (UC52)
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 transition-colors"
            aria-label="Đóng hộp thoại lịch sử giá"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Content */}
        <div className="space-y-4 p-6">
          {/* Service Summary Card */}
          <div className="rounded-xl border border-slate-200 bg-slate-50/70 p-4">
            <h3 className="font-bold text-ink text-sm">{item.name}</h3>
            <p className="mt-1 text-xs text-sub">{item.description}</p>
            <div className="mt-2.5 flex flex-wrap gap-x-5 gap-y-1.5 text-xs text-sub">
              <span>
                Khoa: <strong className="text-ink">{item.department}</strong>
              </span>
              <span>
                Phân loại: <strong className="text-ink">{item.category}</strong>
              </span>
              <span>
                Giá niêm yết hiện tại:{' '}
                <strong className="text-emerald-700">{formatVnd(item.currentPrice)}</strong>
              </span>
            </div>
          </div>

          {/* Chronological History Table */}
          <div>
            <h4 className="mb-2 flex items-center gap-1.5 text-xs font-bold text-ink">
              <Calendar className="h-4 w-4 text-primary" aria-hidden="true" />
              Lịch sử các quyết định và mức giá phê duyệt:
            </h4>
            <div className="overflow-hidden rounded-lg border border-border">
              <table className="w-full text-left text-xs">
                <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wider text-sub">
                  <tr>
                    <th className="px-3.5 py-2.5">Ngày hiệu lực</th>
                    <th className="px-3.5 py-2.5">Số Quyết định</th>
                    <th className="px-3.5 py-2.5 text-right">Mức giá</th>
                    <th className="px-3.5 py-2.5 text-center">Biến động</th>
                    <th className="px-3.5 py-2.5">Cơ quan phê duyệt</th>
                    <th className="px-3.5 py-2.5">Ghi chú điều chỉnh</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {history.map((record, idx) => {
                    const nextRecord = history[idx + 1];
                    let diffPercent: number | null = null;
                    if (nextRecord && nextRecord.price > 0) {
                      diffPercent = Math.round(
                        ((record.price - nextRecord.price) / nextRecord.price) * 100,
                      );
                    }

                    return (
                      <tr key={idx} className="hover:bg-slate-50/50">
                        <td className="px-3.5 py-2.5 font-medium text-slate-800 whitespace-nowrap">
                          {record.date}
                        </td>
                        <td className="px-3.5 py-2.5 font-mono text-[11px] font-semibold text-slate-700 whitespace-nowrap">
                          {record.decisionNumber}
                        </td>
                        <td className="px-3.5 py-2.5 text-right font-bold text-emerald-700 whitespace-nowrap">
                          {formatVnd(record.price)}
                        </td>
                        <td className="px-3.5 py-2.5 text-center whitespace-nowrap">
                          {diffPercent !== null ? (
                            diffPercent > 0 ? (
                              <span className="inline-flex items-center gap-0.5 text-[10px] font-bold text-rose-600">
                                <TrendingUp className="h-3 w-3" aria-hidden="true" />+{diffPercent}%
                              </span>
                            ) : diffPercent < 0 ? (
                              <span className="inline-flex items-center gap-0.5 text-[10px] font-bold text-emerald-600">
                                <TrendingDown className="h-3 w-3" aria-hidden="true" />
                                {diffPercent}%
                              </span>
                            ) : (
                              <span className="text-[10px] text-slate-400">0%</span>
                            )
                          ) : (
                            <span className="text-[10px] text-slate-400">—</span>
                          )}
                        </td>
                        <td className="px-3.5 py-2.5 text-[11px] text-slate-600">
                          {record.appliedBy}
                        </td>
                        <td className="px-3.5 py-2.5 text-[11px] text-sub max-w-xs">
                          {record.note}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          {/* UC52 Legal Notice Card */}
          <div className="flex items-start gap-2.5 rounded-lg border border-sky-100 bg-sky-50/70 p-3.5 text-xs text-sky-950">
            <AlertCircle className="h-4 w-4 shrink-0 text-primary mt-0.5" aria-hidden="true" />
            <div>
              <div className="font-bold text-primary">
                Quy chuẩn quản lý Biểu phí cơ sở (UC52 / Issue #205):
              </div>
              <p className="mt-0.5 text-[11px] text-sky-900 leading-relaxed">
                Màn hình này thực hiện chức năng tra cứu và niêm yết công khai bảng giá dịch vụ khám
                chữa bệnh ngoại trú theo quy chuẩn quản lý viện phí. Các thao tác cập nhật biểu phí,
                điều chỉnh giá theo văn bản mới hoặc thêm phiên bản biểu phí sẽ được thực hiện tại
                quy trình Quản lý Cập nhật Biểu phí (Issue #207) thông qua Hội đồng Giá Bệnh viện.
              </p>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end border-t border-border bg-slate-50 px-6 py-3.5 rounded-b-xl">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50"
          >
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
