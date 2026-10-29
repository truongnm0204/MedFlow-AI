import { Badge } from '@/shared/ui/badge';

import type {
  PaymentVerificationStatus,
  PrescriptionQueueEntry,
  PrescriptionQueueStatus,
} from '../types/pharmacy.types';

interface PharmacyQueueTableProps {
  entries: PrescriptionQueueEntry[];
  selectedPrescriptionId?: string | null;
  onSelectPrescription?: (entry: PrescriptionQueueEntry) => void;
}

const STATUS_META: Record<
  PrescriptionQueueStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' }
> = {
  PENDING: { label: 'Chờ soạn', tone: 'amber' },
  PREPARING: { label: 'Đang soạn', tone: 'sky' },
  READY: { label: 'Chờ phát', tone: 'emerald' },
  DISPENSED: { label: 'Đã phát', tone: 'slate' },
};

const PAYMENT_META: Record<
  PaymentVerificationStatus,
  { label: string; tone: 'emerald' | 'amber' }
> = {
  PAID_QR: { label: 'Đã TT QR • Đủ lô', tone: 'emerald' },
  PENDING_PAYMENT: { label: 'Chờ thanh toán', tone: 'amber' },
};

export function PharmacyQueueTable({
  entries,
  selectedPrescriptionId,
  onSelectPrescription,
}: PharmacyQueueTableProps) {
  if (entries.length === 0) {
    return (
      <div className="py-10 text-center text-xs text-sub">
        Không tìm thấy đơn thuốc phù hợp với bộ lọc hiện tại.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs">
        <thead>
          <tr className="border-b border-border font-mono text-[10px] tracking-wider uppercase text-sub">
            <th className="py-2.5 pr-3">Mã đơn / STT</th>
            <th className="py-2.5 pr-3">Bệnh nhân</th>
            <th className="py-2.5 pr-3">Bác sĩ / Khoa</th>
            <th className="py-2.5 pr-3">Lô FEFO / TT</th>
            <th className="py-2.5 text-right">Trạng thái</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {entries.map((entry) => {
            const statusMeta = STATUS_META[entry.status];
            const paymentMeta = PAYMENT_META[entry.paymentStatus];
            const isSelected = selectedPrescriptionId === entry.id;

            return (
              <tr
                key={entry.id}
                onClick={() => onSelectPrescription?.(entry)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/70' : 'hover:bg-slate-50/80'
                }`}
              >
                <td className="py-3 pr-3 whitespace-nowrap">
                  <div className="font-mono text-xs font-bold text-primary">
                    {entry.prescriptionCode}
                  </div>
                  <div className="font-mono text-[10px] text-sub">
                    #{entry.ticketNo} • {entry.prescribedAt}
                  </div>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-semibold text-ink">{entry.patientName}</div>
                  <div className="font-mono text-[10px] text-sub">
                    {entry.patientCode} • {entry.patientAge}t
                  </div>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-medium text-ink">{entry.prescribingDoctor}</div>
                  <div className="text-[11px] text-sub">
                    {entry.departmentRoom} ({entry.itemCount} thuốc)
                  </div>
                </td>
                <td className="py-3 pr-3 whitespace-nowrap">
                  <div>
                    {entry.stockTransactionCode ? (
                      <Badge tone="slate" className="font-mono">
                        {entry.stockTransactionCode}
                      </Badge>
                    ) : (
                      <Badge tone={paymentMeta.tone}>{paymentMeta.label}</Badge>
                    )}
                  </div>
                  <div className="mt-1 font-mono text-[10px] text-sub">{entry.fefoSummary}</div>
                </td>
                <td className="py-3 text-right whitespace-nowrap">
                  <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
