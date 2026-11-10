import { ArrowRight, CheckCircle2, ShieldAlert, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type {
  PrescriptionSearchItem,
  PrescriptionSearchStatus,
} from '../types/prescription-search.types';

interface PrescriptionSearchTableProps {
  items: PrescriptionSearchItem[];
  onPreparePrescription: (item: PrescriptionSearchItem) => void;
}

const STATUS_BADGE: Record<
  PrescriptionSearchStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' }
> = {
  PENDING_APPROVAL: { label: 'Chờ tiếp nhận', tone: 'amber' },
  PREPARING: { label: 'Đang soạn thuốc', tone: 'sky' },
  READY_FOR_DISPENSE: { label: 'Sẵn sàng phát', tone: 'emerald' },
  COMPLETED: { label: 'Đã cấp phát', tone: 'slate' },
};

export function PrescriptionSearchTable({
  items,
  onPreparePrescription,
}: PrescriptionSearchTableProps) {
  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-2xl border border-border bg-white shadow-2xs">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className="border-b border-border bg-slate-50/80 text-[11px] font-bold uppercase tracking-wider text-sub">
                <th scope="col" className="px-4 py-3.5">
                  STT
                </th>
                <th scope="col" className="px-4 py-3.5">
                  MÃ ĐƠN &amp; GIỜ
                </th>
                <th scope="col" className="px-4 py-3.5">
                  BỆNH NHÂN &amp; MÃ BN
                </th>
                <th scope="col" className="px-4 py-3.5">
                  BÁC SĨ &amp; BUỒNG KHÁM
                </th>
                <th scope="col" className="px-4 py-3.5">
                  CHẨN ĐOÁN &amp; CẢNH BÁO DỊ ỨNG
                </th>
                <th scope="col" className="px-4 py-3.5">
                  SỐ KHOẢN THUỐC
                </th>
                <th scope="col" className="px-4 py-3.5">
                  TỔNG TIỀN THUỐC
                </th>
                <th scope="col" className="px-4 py-3.5 text-right">
                  THAO TÁC
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-5 py-10 text-center text-sub">
                    Không tìm thấy đơn thuốc nào khớp với điều kiện lọc hiện tại.
                  </td>
                </tr>
              ) : (
                items.map((item) => {
                  const statusMeta = STATUS_BADGE[item.status];
                  return (
                    <tr key={item.id} className="transition-colors hover:bg-slate-50/70">
                      <td className="px-4 py-4 font-mono font-bold text-navy">{item.queueNo}</td>

                      <td className="px-4 py-4">
                        <div className="font-mono font-bold text-primary">{item.rxCode}</div>
                        <div className="mt-0.5 text-[11px] text-sub">
                          {item.issuedTime} •{' '}
                          <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                        </div>
                      </td>

                      <td className="px-4 py-4">
                        <div className="font-bold text-navy">{item.patientName}</div>
                        <div className="mt-0.5 font-mono text-[11px] text-sub">
                          {item.patientCode} • {item.gender}, {item.age}t
                        </div>
                      </td>

                      <td className="px-4 py-4">
                        <div className="font-semibold text-ink">{item.doctorName}</div>
                        <div className="mt-0.5 text-[11px] text-sub">{item.roomLabel}</div>
                      </td>

                      <td className="px-4 py-4 space-y-1">
                        <div className="font-medium text-ink">{item.diagnosisText}</div>
                        {item.allergyWarning ? (
                          <div className="inline-flex items-center gap-1 rounded bg-rose-50 px-2 py-0.5 text-[10px] font-bold text-rose-700 border border-rose-200">
                            <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
                            <span>{item.allergyWarning}</span>
                          </div>
                        ) : (
                          <div className="inline-flex items-center gap-1 text-[11px] text-emerald-700">
                            <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                            <span>An toàn tương tác</span>
                          </div>
                        )}
                      </td>

                      <td className="px-4 py-4 whitespace-nowrap">
                        <span className="font-bold text-navy">{item.itemCount} khoản</span>{' '}
                        <span className="text-sub">({item.totalUnits} viên)</span>
                      </td>

                      <td className="px-4 py-4 font-mono font-bold text-navy whitespace-nowrap">
                        {item.totalPriceFormatted}
                      </td>

                      <td className="px-4 py-4 text-right whitespace-nowrap">
                        <Button
                          type="button"
                          variant={item.status === 'PENDING_APPROVAL' ? 'primary' : 'secondary'}
                          size="sm"
                          onClick={() => onPreparePrescription(item)}
                          className="gap-1.5"
                        >
                          <span>
                            {item.status === 'PENDING_APPROVAL'
                              ? 'Tiếp nhận & Soạn thuốc'
                              : 'Xem chi tiết'}
                          </span>
                          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                        </Button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* GPP Compliance Notice */}
      <div className="flex items-start gap-3 rounded-xl border border-sky-200 bg-sky-50/70 p-4 text-xs text-sky-900">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <div>
          <p className="font-bold">Quy chuẩn Dược lâm sàng &amp; An toàn Cấp phát GPP</p>
          <p className="mt-0.5 text-sub">
            Nhà thuốc bệnh viện MedFlow tuân thủ nghiêm ngặt quy trình đối chiếu đơn thuốc 5 đúng,
            kiểm tra cảnh báo dị ứng thuốc và áp dụng nguyên tắc xuất kho FEFO (hạn dùng gần xuất
            trước).
          </p>
        </div>
      </div>
    </div>
  );
}
