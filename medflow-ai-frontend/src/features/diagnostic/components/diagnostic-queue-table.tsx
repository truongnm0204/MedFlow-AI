import { Badge } from '@/shared/ui/badge';

import type {
  DiagnosticCategory,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticQueueEntry,
} from '../types/diagnostic.types';

interface DiagnosticQueueTableProps {
  entries: DiagnosticQueueEntry[];
}

const STATUS_META: Record<
  DiagnosticOrderStatus,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' | 'rose' }
> = {
  ORDERED: { label: 'Chờ xác nhận', tone: 'amber' },
  CONFIRMED: { label: 'Đã tiếp nhận', tone: 'sky' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'emerald' },
  COMPLETED: { label: 'Đã có KQ', tone: 'slate' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
};

const CATEGORY_META: Record<
  DiagnosticCategory,
  { label: string; tone: 'sky' | 'emerald' | 'amber' }
> = {
  LABORATORY: { label: 'Xét nghiệm', tone: 'sky' },
  IMAGING: { label: 'CĐ hình ảnh', tone: 'emerald' },
  FUNCTIONAL: { label: 'Thăm dò CN', tone: 'amber' },
};

const PRIORITY_META: Record<
  DiagnosticPriority,
  { label: string; tone: 'slate' | 'amber' | 'rose' }
> = {
  NORMAL: { label: 'Bình thường', tone: 'slate' },
  URGENT: { label: 'Khẩn', tone: 'amber' },
  STAT: { label: 'Cấp cứu STAT', tone: 'rose' },
};

export function DiagnosticQueueTable({ entries }: DiagnosticQueueTableProps) {
  if (entries.length === 0) {
    return (
      <div className="py-10 text-center text-xs text-slate-500">
        Không tìm thấy chỉ định cận lâm sàng phù hợp với bộ lọc.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-200">
            <th className="py-2 pr-3">Số / Mã CĐ</th>
            <th className="py-2 pr-3">Bệnh nhân</th>
            <th className="py-2 pr-3">Dịch vụ CLS</th>
            <th className="py-2 pr-3">Nhóm</th>
            <th className="py-2 pr-3">Phòng</th>
            <th className="py-2 pr-3">BS chỉ định</th>
            <th className="py-2 pr-3">Ưu tiên</th>
            <th className="py-2">Trạng thái</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr
              key={entry.id}
              className="border-b border-slate-100 last:border-0 hover:bg-slate-50"
            >
              <td className="py-2.5 pr-3">
                <div className="font-mono font-bold text-slate-800">{entry.ticketNo}</div>
                <div className="text-[10px] font-mono text-slate-400">{entry.orderCode}</div>
              </td>
              <td className="py-2.5 pr-3">
                <div className="font-semibold text-slate-800">{entry.patientName}</div>
                <div className="text-[10px] text-slate-400 font-mono">{entry.patientCode}</div>
              </td>
              <td className="py-2.5 pr-3 font-medium text-slate-800">{entry.serviceName}</td>
              <td className="py-2.5 pr-3">
                <Badge tone={CATEGORY_META[entry.category].tone}>
                  {CATEGORY_META[entry.category].label}
                </Badge>
              </td>
              <td className="py-2.5 pr-3 font-mono text-slate-600">{entry.room}</td>
              <td className="py-2.5 pr-3">
                <div className="text-slate-700">{entry.orderingDoctor}</div>
                <div className="text-[10px] tabular-nums text-slate-400">{entry.orderedAt}</div>
              </td>
              <td className="py-2.5 pr-3">
                <Badge tone={PRIORITY_META[entry.priority].tone}>
                  {PRIORITY_META[entry.priority].label}
                </Badge>
              </td>
              <td className="py-2.5">
                <Badge tone={STATUS_META[entry.status].tone}>
                  {STATUS_META[entry.status].label}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
