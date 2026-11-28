import { AlertCircle, Calendar, DollarSign, X } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import { useMedicinePriceHistory } from '../hooks/use-medicine-prices';
import { useMedicinePriceStore } from '../store/medicine-price.store';
import type { MedicinePriceHistoryEntry } from '../types/medicine-price.types';

export function MedicinePriceHistoryModal() {
  const medicine = useMedicinePriceStore((state) => state.priceHistoryMedicine);
  const setPriceHistoryMedicine = useMedicinePriceStore((state) => state.setPriceHistoryMedicine);
  const setUpdatePriceMedicine = useMedicinePriceStore((state) => state.setUpdatePriceMedicine);

  const historyQuery = useMedicinePriceHistory(
    medicine ? medicine.medicineCode || String(medicine.id) : null,
    medicine?.hospitalId ?? undefined,
  );

  if (!medicine) return null;

  const fetchedHistory = historyQuery.data ?? [];
  const history: MedicinePriceHistoryEntry[] =
    fetchedHistory.length > 0 ? fetchedHistory : medicine.priceHistory;

  const medicineMeta = [
    medicine.activeIngredient,
    medicine.strength,
    medicine.dosageForm,
    medicine.dispensingUnit,
  ]
    .filter((value): value is string => Boolean(value))
    .join(' • ');

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="medicine-price-history-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-2xl animate-in zoom-in-95 rounded-xl border border-border bg-white shadow-xl fade-in duration-150">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <span className="rounded bg-sky-50 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">
              {medicine.medicineCode || `#${medicine.id}`}
            </span>
            <h2 id="medicine-price-history-modal-title" className="mt-1 text-sm font-bold text-ink">
              Lịch sử giá thuốc
            </h2>
          </div>
          <button
            type="button"
            onClick={() => setPriceHistoryMedicine(null)}
            className="rounded p-1 text-slate-400 hover:text-slate-600"
            aria-label="Đóng"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        <div className="space-y-4 p-6">
          <div className="rounded-lg border border-slate-100 bg-slate-50/70 p-3.5 text-xs">
            <div className="text-sm font-bold text-ink">{medicine.name}</div>
            <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sub">
              {medicineMeta && <span>{medicineMeta}</span>}
              <span>
                Cơ sở: <b>{medicine.facility}</b>
              </span>
              <span>
                Giá hiện hành:{' '}
                <b className="text-emerald-700">
                  {medicine.hasEffectivePrice ? medicine.priceFormatted : 'Chưa có giá'}
                </b>
              </span>
            </div>
          </div>

          <div>
            <h3 className="mb-2 flex items-center gap-1.5 text-xs font-bold text-ink">
              <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              Lịch sử các quyết định áp dụng giá:
            </h3>
            <div className="overflow-x-auto rounded-lg border border-border">
              <table className="w-full min-w-[640px] text-left text-xs">
                <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase text-sub">
                  <tr>
                    <th className="px-3 py-2.5">Ngày áp dụng</th>
                    <th className="px-3 py-2.5">Mức giá</th>
                    <th className="px-3 py-2.5">Hiệu lực</th>
                    <th className="px-3 py-2.5">Số Quyết định</th>
                    <th className="px-3 py-2.5">Người phê duyệt</th>
                    <th className="px-3 py-2.5">Trạng thái</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {historyQuery.isPending && history.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="px-3 py-6 text-center text-sub">
                        Đang tải lịch sử giá thuốc...
                      </td>
                    </tr>
                  ) : history.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="px-3 py-6 text-center text-sub">
                        Chưa có lịch sử quyết định giá được ghi nhận.
                      </td>
                    </tr>
                  ) : (
                    history.map((item) => (
                      <tr key={item.id} className="hover:bg-slate-50/50">
                        <td className="px-3 py-2.5 font-medium text-slate-800">{item.date}</td>
                        <td className="px-3 py-2.5 font-bold text-emerald-700">
                          {item.priceFormatted || `${item.priceAmount.toLocaleString('vi-VN')} đ`}
                        </td>
                        <td className="px-3 py-2.5 font-mono text-[11px] text-slate-600">
                          {item.validFrom ? item.validFrom.slice(0, 10) : '—'} -{' '}
                          {item.validTo ? item.validTo.slice(0, 10) : '—'}
                        </td>
                        <td className="px-3 py-2.5 font-mono text-[11px] text-slate-600">
                          {item.decisionNumber ?? 'Chưa cập nhật'}
                        </td>
                        <td className="px-3 py-2.5 text-[11px] text-sub">
                          {item.appliedBy ?? 'Chưa cập nhật'}
                        </td>
                        <td className="px-3 py-2.5">
                          <span
                            className={`inline-block rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                              item.isActive
                                ? 'bg-emerald-100 text-emerald-800'
                                : 'bg-slate-100 text-slate-600'
                            }`}
                          >
                            {item.isActive ? 'Đang áp dụng' : 'Hết hiệu lực'}
                          </span>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          <div className="flex items-start gap-2.5 rounded-lg border border-sky-100 bg-sky-50/60 p-3 text-[11px] text-sky-900">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <div>
              <p className="font-bold">Quy tắc quản lý giá thuốc:</p>
              <p className="mt-0.5 leading-relaxed text-sky-800">
                Giá thuốc được quản trị tập trung theo chính sách bệnh viện. Mỗi lần điều chỉnh sẽ
                đóng hiệu lực bản ghi cũ và lưu vết quyết định mới.
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center justify-between rounded-b-xl border-t border-border bg-slate-50 px-6 py-3">
          <button
            type="button"
            onClick={() => setPriceHistoryMedicine(null)}
            className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-xs transition-colors hover:bg-slate-50"
          >
            Đóng
          </button>
          <Button
            size="sm"
            className="gap-1.5"
            onClick={() => {
              const target = medicine;
              setPriceHistoryMedicine(null);
              setUpdatePriceMedicine(target);
            }}
          >
            <DollarSign className="h-4 w-4" aria-hidden="true" />
            <span>Cập nhật giá mới</span>
          </Button>
        </div>
      </div>
    </div>
  );
}
