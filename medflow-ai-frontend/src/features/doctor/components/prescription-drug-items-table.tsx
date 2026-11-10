import { Minus, Pill, Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { useState } from 'react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  PrescriptionCatalogOption,
  PrescriptionDrugLineItem,
} from '../types/create-prescription.types';

interface PrescriptionDrugItemsTableProps {
  items: PrescriptionDrugLineItem[];
  catalog: PrescriptionCatalogOption[];
  onQuantityChange: (itemId: string, nextQty: number) => void;
  onDosageChange: (itemId: string, nextDosage: string) => void;
  onRemoveItem: (itemId: string) => void;
  onAddCatalogItem: (option: PrescriptionCatalogOption) => void;
}

function formatVnd(amount: number): string {
  return `${amount.toLocaleString('vi-VN')} đ`;
}

export function PrescriptionDrugItemsTable({
  items,
  catalog,
  onQuantityChange,
  onDosageChange,
  onRemoveItem,
  onAddCatalogItem,
}: PrescriptionDrugItemsTableProps) {
  const [selectedCatalogCode, setSelectedCatalogCode] = useState('');

  const handleAdd = () => {
    const found = catalog.find((c) => c.drugCode === selectedCatalogCode);
    if (!found) return;
    onAddCatalogItem(found);
    setSelectedCatalogCode('');
  };

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-3 py-3.5">
        <div className="flex items-center gap-2">
          <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">
            DANH MỤC THUỐC TRONG ĐƠN ĐIỀU TRỊ NGOẠI TRÚ ({items.length} KHOẢN)
          </h2>
          <Badge tone="emerald">100% Khám Tự nguyện (Không BHYT)</Badge>
        </div>

        <div className="flex items-center gap-2">
          <select
            value={selectedCatalogCode}
            onChange={(e) => setSelectedCatalogCode(e.target.value)}
            aria-label="Chọn thuốc bổ sung vào đơn"
            className="h-9 rounded-lg border border-border bg-canvas px-3 text-xs text-ink focus:bg-white focus:outline-hidden focus:ring-2 focus:ring-primary"
          >
            <option value="">-- Chọn thuốc bổ sung --</option>
            {catalog.map((c) => (
              <option key={c.drugCode} value={c.drugCode}>
                {c.drugName} ({formatVnd(c.unitPrice)}/{c.unit})
              </option>
            ))}
          </select>
          <button
            type="button"
            disabled={!selectedCatalogCode}
            onClick={handleAdd}
            className="inline-flex h-9 items-center gap-1 rounded-lg bg-primary px-3 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark disabled:opacity-40"
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Thêm thuốc mới</span>
          </button>
        </div>
      </CardHeader>

      <CardContent className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className="border-b border-border bg-slate-50/80 text-[11px] font-bold uppercase tracking-wider text-sub">
                <th scope="col" className="px-4 py-3">
                  STT
                </th>
                <th scope="col" className="px-4 py-3">
                  TÊN THUỐC &amp; BIỆT DƯỢC / HOẠT CHẤT
                </th>
                <th scope="col" className="px-4 py-3">
                  DẠNG BÀO CHẾ
                </th>
                <th scope="col" className="px-4 py-3 text-center">
                  SỐ LƯỢNG
                </th>
                <th scope="col" className="px-4 py-3">
                  LIỀU DÙNG &amp; HƯỚNG DẪN SỬ DỤNG CHI TIẾT
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  ĐƠN GIÁ NIÊM YẾT
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  THÀNH TIỀN
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  THAO TÁC
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-4 py-8 text-center text-sub">
                    Chưa có thuốc nào trong đơn. Vui lòng chọn thuốc bổ sung ở thanh phía trên.
                  </td>
                </tr>
              ) : (
                items.map((item, idx) => (
                  <tr key={item.id} className="transition-colors hover:bg-slate-50/60">
                    <td className="px-4 py-4 font-mono font-bold text-navy">0{idx + 1}</td>

                    <td className="px-4 py-4 max-w-[240px]">
                      <div className="font-bold text-navy">{item.drugName}</div>
                      <div className="text-[11px] text-sub">{item.activeIngredient}</div>
                      {item.warningNote && (
                        <div className="mt-1 inline-flex items-center gap-1 rounded bg-rose-50 px-1.5 py-0.5 text-[10px] font-bold text-rose-700 border border-rose-200">
                          <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
                          <span>{item.warningNote}</span>
                        </div>
                      )}
                    </td>

                    <td className="px-4 py-4 text-sub">{item.dosageForm}</td>

                    <td className="px-4 py-4">
                      <div className="flex items-center justify-center gap-1.5">
                        <button
                          type="button"
                          onClick={() => onQuantityChange(item.id, Math.max(1, item.quantity - 1))}
                          aria-label={`Giảm số lượng ${item.drugName}`}
                          className="inline-flex h-6 w-6 items-center justify-center rounded border border-border bg-white text-ink hover:bg-canvas"
                        >
                          <Minus className="h-3 w-3" aria-hidden="true" />
                        </button>
                        <span className="w-10 text-center font-mono font-bold text-navy">
                          {item.quantity}
                        </span>
                        <button
                          type="button"
                          onClick={() => onQuantityChange(item.id, item.quantity + 1)}
                          aria-label={`Tăng số lượng ${item.drugName}`}
                          className="inline-flex h-6 w-6 items-center justify-center rounded border border-border bg-white text-ink hover:bg-canvas"
                        >
                          <Plus className="h-3 w-3" aria-hidden="true" />
                        </button>
                      </div>
                      <div className="mt-0.5 text-center text-[10px] text-sub">{item.unit}</div>
                    </td>

                    <td className="px-4 py-4 min-w-[230px]">
                      <input
                        type="text"
                        value={item.dosageInstruction}
                        aria-label={`Liều dùng ${item.drugName}`}
                        onChange={(e) => onDosageChange(item.id, e.target.value)}
                        className="w-full rounded-lg border border-border bg-white px-2.5 py-1 text-xs font-semibold text-navy focus:outline-hidden focus:ring-2 focus:ring-primary"
                      />
                      <div className="mt-1 text-[11px] text-sub">{item.mealTiming}</div>
                    </td>

                    <td className="px-4 py-4 text-right font-mono text-sub whitespace-nowrap">
                      {formatVnd(item.unitPrice)}
                    </td>

                    <td className="px-4 py-4 text-right font-mono font-bold text-navy whitespace-nowrap">
                      {formatVnd(item.unitPrice * item.quantity)}
                    </td>

                    <td className="px-4 py-4 text-right">
                      <button
                        type="button"
                        onClick={() => onRemoveItem(item.id)}
                        aria-label={`Xóa thuốc ${item.drugName}`}
                        className="rounded-lg p-1.5 text-sub transition-colors hover:bg-rose-50 hover:text-rose-600"
                      >
                        <Trash2 className="h-4 w-4" aria-hidden="true" />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}
