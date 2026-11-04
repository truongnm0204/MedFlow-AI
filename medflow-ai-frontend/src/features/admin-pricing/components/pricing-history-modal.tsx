import { Calendar, TrendingDown, TrendingUp, X } from 'lucide-react';
import { useEffect } from 'react';

import type { ServicePricingItem } from '../types/service-pricing.types';

interface PricingHistoryModalProps {
  item: ServicePricingItem | null;
  onClose: () => void;
}

export function PricingHistoryModal({ item, onClose }: PricingHistoryModalProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
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
      <div className="w-full max-w-xl rounded-xl border border-border bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-border px-5 py-3.5">
          <div>
            <span className="font-mono text-[10px] font-bold text-primary">{item.code}</span>
            <h2 id="pricing-history-title" className="text-sm font-bold text-ink">
              Lịch Sử Biểu Phí & Quyết Định Áp Dụng (UC52)
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại lịch sử giá"
            className="rounded p-1 text-slate-400 hover:text-slate-600"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="p-5 space-y-3.5 text-xs">
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
            <div className="font-bold text-ink">{item.name}</div>
            <div className="mt-1 flex gap-4 text-sub text-[11px]">
              <span>
                Khoa: <b>{item.department}</b>
              </span>
              <span>
                Giá hiện tại: <b className="text-emerald-700">{formatVnd(item.currentPrice)}</b>
              </span>
            </div>
          </div>

          <div>
            <div className="mb-2 flex items-center gap-1.5 font-bold text-ink">
              <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Dòng thời gian các quyết định giá:</span>
            </div>
            <div className="overflow-hidden rounded-lg border border-border">
              <table className="w-full text-left text-xs">
                <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase text-sub">
                  <tr>
                    <th className="px-3 py-2">Ngày hiệu lực</th>
                    <th className="px-3 py-2">Số Quyết định</th>
                    <th className="px-3 py-2 text-right">Mức giá</th>
                    <th className="px-3 py-2 text-center">Biến động</th>
                    <th className="px-3 py-2">Người phê duyệt</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {history.map((record, idx) => {
                    const prev = history[idx + 1];
                    const diff =
                      prev && prev.price > 0
                        ? Math.round(((record.price - prev.price) / prev.price) * 100)
                        : null;
                    return (
                      <tr key={idx} className="hover:bg-slate-50/50">
                        <td className="px-3 py-2 font-medium text-slate-800">{record.date}</td>
                        <td className="px-3 py-2 font-mono text-[11px] text-slate-600">
                          {record.decisionNumber}
                        </td>
                        <td className="px-3 py-2 text-right font-bold text-emerald-700">
                          {formatVnd(record.price)}
                        </td>
                        <td className="px-3 py-2 text-center">
                          {diff !== null ? (
                            diff > 0 ? (
                              <span className="flex items-center justify-center gap-0.5 text-[10px] font-bold text-rose-600">
                                <TrendingUp className="h-3 w-3" />+{diff}%
                              </span>
                            ) : (
                              <span className="flex items-center justify-center gap-0.5 text-[10px] font-bold text-emerald-600">
                                <TrendingDown className="h-3 w-3" />
                                {diff}%
                              </span>
                            )
                          ) : (
                            <span className="text-slate-400 text-[10px]">—</span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-sub text-[11px]">{record.appliedBy}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <div className="flex justify-end border-t border-border bg-slate-50 px-5 py-3 rounded-b-xl">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-border bg-white px-3.5 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
          >
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
