import { Badge } from '@/shared/ui/badge';

import type { QueueEntry, QueueStatus } from '../types/reception.types';

interface QueuePreviewTableProps {
  entries: QueueEntry[];
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

export function QueuePreviewTable({ entries }: QueuePreviewTableProps) {
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
            <th className="py-2">Trạng thái</th>
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
                <div className="font-semibold text-slate-800">{entry.patientName}</div>
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
