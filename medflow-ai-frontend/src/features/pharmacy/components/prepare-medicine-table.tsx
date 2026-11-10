import { CheckCircle2, MapPin, PackageCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { PrepareMedicineItem } from '../types/prepare-medicine.types';

interface PrepareMedicineTableProps {
  items: PrepareMedicineItem[];
  onPickedQtyChange: (itemId: string, qty: number) => void;
  onBatchChange: (itemId: string, batchCode: string) => void;
  onTogglePrepared: (itemId: string) => void;
}

export function PrepareMedicineTable({
  items,
  onPickedQtyChange,
  onBatchChange,
  onTogglePrepared,
}: PrepareMedicineTableProps) {
  const preparedCount = items.filter((i) => i.isPrepared).length;

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
        <div className="flex items-center gap-2">
          <PackageCheck className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">
            Bảng Soạn Thuốc &amp; Kiểm Tra Lô Hạn Dùng Theo Đơn
          </h2>
          <Badge tone={preparedCount === items.length ? 'emerald' : 'sky'}>
            {preparedCount}/{items.length} Khoản đã soạn
          </Badge>
        </div>
        <span className="text-[11px] font-semibold text-sub">
          Quy tắc kho: <strong className="text-navy">FEFO (Hạn gần xuất trước)</strong> • Kiểm định
          GPP
        </span>
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
                  TÊN THUỐC &amp; DẠNG BÀO CHẾ
                </th>
                <th scope="col" className="px-4 py-3 text-center">
                  SL KÊ
                </th>
                <th scope="col" className="px-4 py-3 text-center">
                  SL THỰC SOẠN
                </th>
                <th scope="col" className="px-4 py-3 text-center">
                  TỒN KHO KHẢ DỤNG
                </th>
                <th scope="col" className="px-4 py-3">
                  LÔ &amp; HẠN DÙNG (BATCH &amp; EXPIRY)
                </th>
                <th scope="col" className="px-4 py-3">
                  VỊ TRÍ KHO KỆ
                </th>
                <th scope="col" className="px-4 py-3">
                  TRẠNG THÁI
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  THAO TÁC
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.map((item) => {
                const selectedBatch =
                  item.availableBatches.find((b) => b.batchCode === item.selectedBatchCode) ??
                  item.availableBatches[0];
                const isQtyMatched = item.pickedQty === item.prescribedQty;

                return (
                  <tr
                    key={item.id}
                    className={`transition-colors ${
                      item.isPrepared ? 'bg-emerald-50/30' : 'hover:bg-slate-50/60'
                    }`}
                  >
                    <td className="px-4 py-4 font-mono font-bold text-navy">0{item.orderIndex}</td>

                    <td className="px-4 py-4 max-w-[260px]">
                      <div className="font-bold text-navy">{item.drugName}</div>
                      <div className="mt-0.5 text-[11px] text-sub">{item.dosageForm}</div>
                      <div className="mt-1 text-[11px] text-sky-700 font-medium">
                        {item.usageInstruction}
                      </div>
                    </td>

                    <td className="px-4 py-4 text-center font-mono font-bold text-navy whitespace-nowrap">
                      {item.prescribedQty} {item.unit}
                    </td>

                    <td className="px-4 py-4 text-center">
                      <input
                        type="number"
                        min={0}
                        max={selectedBatch.availableStock}
                        value={item.pickedQty}
                        aria-label={`Số lượng thực soạn ${item.drugName}`}
                        onChange={(e) => onPickedQtyChange(item.id, Number(e.target.value) || 0)}
                        className={`h-8 w-16 rounded-lg border px-2 text-center font-mono text-xs font-bold focus:outline-hidden focus:ring-2 focus:ring-primary ${
                          isQtyMatched
                            ? 'border-border bg-white text-navy'
                            : 'border-rose-400 bg-rose-50 text-rose-700'
                        }`}
                      />
                    </td>

                    <td className="px-4 py-4 text-center font-mono text-ink whitespace-nowrap">
                      {selectedBatch.availableStock} {item.unit}
                    </td>

                    <td className="px-4 py-4">
                      <select
                        value={selectedBatch.batchCode}
                        aria-label={`Chọn lô thuốc ${item.drugName}`}
                        onChange={(e) => onBatchChange(item.id, e.target.value)}
                        className="w-full min-w-[180px] rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-[11px] text-navy focus:outline-hidden focus:ring-2 focus:ring-primary"
                      >
                        {item.availableBatches.map((b) => (
                          <option key={b.batchCode} value={b.batchCode}>
                            {b.batchCode} • HSD: {b.expiryDate}
                            {b.isFefoRecommended ? ' (FEFO)' : ''}
                          </option>
                        ))}
                      </select>
                    </td>

                    <td className="px-4 py-4 whitespace-nowrap text-sub">
                      <span className="inline-flex items-center gap-1 font-medium text-ink">
                        <MapPin className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                        {selectedBatch.shelfLocation}
                      </span>
                    </td>

                    <td className="px-4 py-4 whitespace-nowrap">
                      {item.isPrepared ? (
                        <Badge tone="emerald">Đã soạn đủ</Badge>
                      ) : (
                        <Badge tone="amber">Chờ soạn</Badge>
                      )}
                    </td>

                    <td className="px-4 py-4 text-right whitespace-nowrap">
                      <button
                        type="button"
                        onClick={() => onTogglePrepared(item.id)}
                        aria-label={
                          item.isPrepared
                            ? `Bỏ đánh dấu đã soạn ${item.drugName}`
                            : `Xác nhận đã soạn ${item.drugName}`
                        }
                        className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
                          item.isPrepared
                            ? 'border border-emerald-300 bg-emerald-50 text-emerald-800 hover:bg-emerald-100'
                            : 'bg-primary text-white hover:bg-primary-dark shadow-2xs'
                        }`}
                      >
                        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>{item.isPrepared ? 'Đã kiểm' : 'Soạn đủ'}</span>
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}
