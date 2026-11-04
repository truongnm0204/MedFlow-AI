import { Printer } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { QueueEntry, QueueStatus } from '../types/reception.types';

interface QueuePreviewTableProps {
  entries: QueueEntry[];
  onSelectPatient?: (patientCode: string) => void;
  onTransferRoom?: (entry: QueueEntry) => void;
  onPrintTicket?: (entry: QueueEntry) => void;
}

const STATUS_META: Record<
  QueueStatus,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' | 'rose' }
> = {
  WAITING: { label: 'Đang chờ', tone: 'amber' },
  CALLED: { label: 'Đã gọi', tone: 'sky' },
  IN_PROGRESS: { label: 'Đang khám', tone: 'emerald' },
  DONE: { label: 'Xong', tone: 'slate' },
  SKIPPED: { label: 'Bỏ lượt', tone: 'rose' },
};

const PRIORITY_LABEL: Record<QueueEntry['priority'], string> = {
  NORMAL: '',
  URGENT: 'Ưu tiên',
  ELDERLY: 'Người cao tuổi',
  PREGNANT: 'Phụ nữ mang thai',
};

export function QueuePreviewTable({
  entries,
  onSelectPatient,
  onTransferRoom,
  onPrintTicket,
}: QueuePreviewTableProps) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-200">
            <th className="py-2 pr-3">Số</th>
            <th className="py-2 pr-3">Bệnh nhân</th>
            <th className="py-2 pr-3">Loại</th>
            <th className="py-2 pr-3">Phòng</th>
            <th className="py-2 pr-3">Check-in</th>
            <th className="py-2 pr-3">Ưu tiên</th>
            <th className="py-2 pr-3">Trạng thái</th>
            <th className="py-2 pr-3 text-right">Phiếu khám</th>
            {onTransferRoom && <th className="py-2 text-right">Điều phối</th>}
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr
              key={entry.id}
              className="border-b border-slate-100 last:border-0 hover:bg-slate-50"
            >
              <td className="py-2.5 pr-3 font-mono font-bold text-slate-800">{entry.ticketNo}</td>
              <td className="py-2.5 pr-3">
                <button
                  type="button"
                  onClick={() => onSelectPatient?.(entry.patientCode)}
                  aria-label={`Xem chi tiết bệnh nhân ${entry.patientName}`}
                  className="font-semibold text-navy hover:text-primary hover:underline text-left"
                >
                  {entry.patientName}
                </button>
                <div className="text-[10px] text-slate-400 font-mono">{entry.patientCode}</div>
              </td>
              <td className="py-2.5 pr-3">
                <Badge tone={entry.visitType === 'WALK_IN' ? 'rose' : 'sky'}>
                  {entry.visitType === 'WALK_IN' ? 'Walk-in' : 'Hẹn trước'}
                </Badge>
              </td>
              <td className="py-2.5 pr-3 font-mono text-slate-600">{entry.room}</td>
              <td className="py-2.5 pr-3 tabular-nums text-slate-600">{entry.checkedInAt}</td>
              <td className="py-2.5 pr-3 text-slate-500">{PRIORITY_LABEL[entry.priority]}</td>
              <td className="py-2.5 pr-3">
                <Badge tone={STATUS_META[entry.status].tone}>
                  {STATUS_META[entry.status].label}
                </Badge>
              </td>
              <td className="py-2.5 text-right">
                <button
                  type="button"
                  onClick={() => onPrintTicket?.(entry)}
                  aria-label={`In phiếu khám cho ${entry.patientName}`}
                  className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-navy transition-colors hover:border-primary hover:bg-canvas hover:text-primary"
                >
                  <Printer className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>In phiếu</span>
                </button>
              </td>
              {onTransferRoom && (
                <td className="py-2.5 text-right">
                  <button
                    type="button"
                    disabled={entry.status === 'IN_PROGRESS' || entry.status === 'DONE'}
                    onClick={() => onTransferRoom(entry)}
                    aria-label={`Chuyển phòng khám cho ${entry.patientName}`}
                    className="rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-primary hover:bg-canvas focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    Chuyển phòng
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
