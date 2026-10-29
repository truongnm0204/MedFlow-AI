import { AlertCircle, Calendar, X } from 'lucide-react';

import { useExaminationServiceStore } from '../store/examination-service.store';

export function ServicePriceHistoryModal() {
  const service = useExaminationServiceStore((state) => state.priceHistoryService);
  const setService = useExaminationServiceStore((state) => state.setPriceHistoryService);

  if (!service) return null;

  const history = service.priceHistory ?? [
    {
      date: service.effectiveDate ?? '2026-01-01',
      price: service.price,
      decisionNumber: service.decisionNumber ?? 'QĐ-142/BV-KH',
      note: 'Biểu phí hiện hành áp dụng năm 2026',
      appliedBy: 'Hội đồng Giá Bệnh viện',
    },
    {
      date: '2025-01-01',
      price: Math.round(service.price * 0.85),
      decisionNumber: 'QĐ-088/BV-KH',
      note: 'Biểu phí ban hành năm 2025',
      appliedBy: 'Ban Giám đốc',
    },
  ];

  const formatVnd = (num: number) => num.toLocaleString('vi-VN') + ' đ';

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="price-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-xl rounded-xl border border-border bg-white shadow-xl animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <span className="text-[10px] font-mono font-bold text-primary bg-sky-50 px-2 py-0.5 rounded">
              {service.code}
            </span>
            <h2 id="price-modal-title" className="text-sm font-bold text-ink mt-1">
              Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52
            </h2>
          </div>
          <button
            type="button"
            onClick={() => setService(null)}
            className="rounded p-1 text-slate-400 hover:text-slate-600"
            aria-label="Đóng"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-4 p-6">
          <div className="rounded-lg border border-slate-100 bg-slate-50/70 p-3.5 text-xs">
            <div className="font-bold text-ink text-sm">{service.name}</div>
            <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sub">
              <span>
                Khoa: <b>{service.department}</b>
              </span>
              <span>
                Phân loại: <b>{service.category}</b>
              </span>
              <span>
                Giá hiện tại: <b className="text-emerald-700">{formatVnd(service.price)}</b>
              </span>
            </div>
          </div>

          <div>
            <h3 className="text-xs font-bold text-ink mb-2 flex items-center gap-1.5">
              <Calendar className="h-3.5 w-3.5 text-primary" />
              Lịch sử các quyết định áp dụng giá:
            </h3>
            <div className="overflow-hidden rounded-lg border border-border">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 text-[10px] font-bold uppercase text-sub border-b border-border">
                  <tr>
                    <th className="px-3 py-2.5">Ngày áp dụng</th>
                    <th className="px-3 py-2.5">Số Quyết định</th>
                    <th className="px-3 py-2.5">Mức giá</th>
                    <th className="px-3 py-2.5">Người phê duyệt</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {history.map((item, idx) => (
                    <tr key={idx} className="hover:bg-slate-50/50">
                      <td className="px-3 py-2.5 font-medium text-slate-800">{item.date}</td>
                      <td className="px-3 py-2.5 font-mono text-[11px] text-slate-600">
                        {item.decisionNumber}
                      </td>
                      <td className="px-3 py-2.5 font-bold text-emerald-700">
                        {formatVnd(item.price)}
                      </td>
                      <td className="px-3 py-2.5 text-sub text-[11px]">{item.appliedBy}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="rounded-lg border border-sky-100 bg-sky-50/60 p-3 text-[11px] text-sky-900 flex gap-2.5 items-start">
            <AlertCircle className="h-4 w-4 shrink-0 text-primary mt-0.5" />
            <div>
              <p className="font-bold">Quy tắc liên kết UC52 (Quản lý Biểu phí Cơ sở):</p>
              <p className="mt-0.5 text-sky-800 leading-relaxed">
                Biểu phí dịch vụ khám ngoại trú được quản trị và kiểm soát tập trung theo quy trình
                UC52. Mọi đề xuất thay đổi giá sẽ được chuyển sang phân hệ Biểu phí để Hội đồng thẩm
                định phê duyệt.
              </p>
            </div>
          </div>
        </div>

        <div className="flex justify-end border-t border-border px-6 py-3 bg-slate-50 rounded-b-xl">
          <button
            type="button"
            onClick={() => setService(null)}
            className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 transition-colors shadow-xs"
          >
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
