import { CheckCircle2, QrCode, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { DispensingResultDetail } from '../types/dispensing-result.types';

interface DispensingReceiptCardProps {
  detail: DispensingResultDetail;
}

export function DispensingReceiptCard({ detail }: DispensingReceiptCardProps) {
  return (
    <Card className="overflow-hidden border-emerald-200">
      {/* Green Handover Confirmation Banner */}
      <div className="bg-emerald-600 px-6 py-4 text-white flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-white/15">
            <CheckCircle2 className="h-6 w-6" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-sm font-bold uppercase tracking-wide">
              XÁC NHẬN BÀN GIAO THUỐC NGOẠI TRÚ THÀNH CÔNG
            </h2>
            <p className="text-xs text-emerald-100">
              Biên nhận cấp phát &amp; lưu trữ HIS (SCR-PHA-04)
            </p>
          </div>
        </div>
        <Badge tone="emerald" className="bg-white text-emerald-800 font-mono">
          {detail.statusLabel}
        </Badge>
      </div>

      <CardHeader className="border-b border-border bg-slate-50/70 py-4">
        <div className="grid grid-cols-1 gap-4 text-xs sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <span className="text-sub">Mã biên nhận bàn giao:</span>
            <p className="font-mono text-sm font-bold text-navy">{detail.dispenseCode}</p>
          </div>
          <div>
            <span className="text-sub">Mã đơn thuốc tham chiếu:</span>
            <p className="font-mono text-sm font-bold text-primary">{detail.rxCode}</p>
          </div>
          <div>
            <span className="text-sub">Thời gian bàn giao:</span>
            <p className="font-mono font-bold text-ink">{detail.dispensedAt}</p>
          </div>
          <div>
            <span className="text-sub">Dược sĩ thực hiện:</span>
            <p className="font-bold text-navy">{detail.dispensingPharmacist}</p>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-6 p-6">
        {/* Patient & Receiver Summary */}
        <div className="grid grid-cols-1 gap-4 rounded-xl border border-border bg-canvas p-4 text-xs sm:grid-cols-2">
          <div className="space-y-1">
            <p className="text-[11px] font-bold uppercase text-sub">Thông tin Người bệnh</p>
            <p className="text-sm font-bold text-navy">{detail.patientName}</p>
            <p className="font-mono text-sub">
              Mã BN: <strong className="text-ink">{detail.patientCode}</strong> • Lượt khám:{' '}
              <strong className="text-ink">{detail.visitCode}</strong>
            </p>
            <p className="text-sub">Bác sĩ kê đơn: {detail.prescribingDoctor}</p>
          </div>

          <div className="space-y-1 sm:border-l sm:border-border sm:pl-4">
            <p className="text-[11px] font-bold uppercase text-sub">
              Thông tin Người nhận &amp; Thanh toán
            </p>
            <p className="font-bold text-navy">{detail.receiverName}</p>
            <p className="font-mono text-sub">
              CCCD đối chiếu: <strong className="text-ink">{detail.receiverIdentityMasked}</strong>
            </p>
            <p className="font-medium text-emerald-700">{detail.paymentSummary}</p>
          </div>
        </div>

        {/* Dispensed Batches Table */}
        <div className="overflow-x-auto rounded-xl border border-border">
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className="border-b border-border bg-slate-50 text-[11px] font-bold uppercase tracking-wider text-sub">
                <th scope="col" className="px-4 py-3">
                  STT
                </th>
                <th scope="col" className="px-4 py-3">
                  TÊN THUỐC &amp; DẠNG BÀO CHẾ
                </th>
                <th scope="col" className="px-4 py-3">
                  SỐ LÔ / HSD (FEFO)
                </th>
                <th scope="col" className="px-4 py-3 text-center">
                  SL BÀN GIAO
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  ĐƠN GIÁ
                </th>
                <th scope="col" className="px-4 py-3 text-right">
                  THÀNH TIỀN
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {detail.items.map((item) => (
                <tr key={item.id}>
                  <td className="px-4 py-3 font-mono font-bold text-navy">0{item.orderIndex}</td>
                  <td className="px-4 py-3">
                    <div className="font-bold text-navy">{item.drugName}</div>
                    <div className="text-[11px] text-sub">{item.dosageForm}</div>
                  </td>
                  <td className="px-4 py-3 font-mono text-ink">
                    <span className="font-bold text-primary">{item.batchCode}</span> • HSD{' '}
                    {item.expiryDate}
                  </td>
                  <td className="px-4 py-3 text-center font-mono font-bold text-navy">
                    {item.qtyDispensed} {item.unit}
                  </td>
                  <td className="px-4 py-3 text-right font-mono text-sub">
                    {item.unitPriceFormatted}
                  </td>
                  <td className="px-4 py-3 text-right font-mono font-bold text-navy">
                    {item.lineTotalFormatted}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-border bg-slate-50/70">
                <td colSpan={5} className="px-4 py-3 text-right font-bold text-navy">
                  TỔNG GIÁ TRỊ ĐƠN THUỐC ĐÃ CẤP PHÁT:
                </td>
                <td className="px-4 py-3 text-right font-mono text-base font-black text-primary">
                  {detail.totalAmountFormatted}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>

        {/* Audit & Digital Signature Footer */}
        <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-sky-200 bg-sky-50/70 p-4 text-xs">
          <div className="flex items-start gap-2.5 max-w-2xl">
            <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <div>
              <p className="font-bold text-navy">Trạng thái Khấu trừ Tồn kho &amp; Lưu trữ HIS</p>
              <p className="mt-0.5 text-sub">{detail.auditNote}</p>
            </div>
          </div>

          <div className="flex items-center gap-3 rounded-xl border border-border bg-white px-3.5 py-2">
            <QrCode className="h-8 w-8 text-navy" aria-hidden="true" />
            <div className="text-[11px]">
              <p className="font-mono font-bold text-navy">{detail.dispenseCode}</p>
              <p className="text-sub">Xác thực điện tử GPP</p>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
