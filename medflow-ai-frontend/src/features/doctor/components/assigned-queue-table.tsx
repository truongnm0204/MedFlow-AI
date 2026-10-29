import { Badge } from '@/shared/ui/badge';

import type {
  AssignedPatientEntry,
  AssignedQueueStatus,
  VisitPriority,
} from '../types/doctor.types';

interface AssignedQueueTableProps {
  entries: AssignedPatientEntry[];
  onSelectPatient?: (entry: AssignedPatientEntry) => void;
}

const STATUS_META: Record<
  AssignedQueueStatus,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' | 'rose' }
> = {
  WAITING: { label: 'Chờ gọi', tone: 'amber' },
  CALLED: { label: 'Đã gọi vào', tone: 'sky' },
  IN_CONSULTATION: { label: 'Đang khám', tone: 'emerald' },
  AWAITING_RESULTS: { label: 'Chờ CLS', tone: 'rose' },
  COMPLETED: { label: 'Đã hoàn tất', tone: 'slate' },
};

const PRIORITY_META: Record<
  VisitPriority,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' | 'rose' } | null
> = {
  NORMAL: null,
  URGENT: { label: 'Cấp cứu / Ưu tiên', tone: 'rose' },
  ELDERLY: { label: 'Người cao tuổi', tone: 'amber' },
  RETURN_RESULT: { label: 'Đọc kết quả CLS', tone: 'sky' },
};

export function AssignedQueueTable({ entries, onSelectPatient }: AssignedQueueTableProps) {
  if (entries.length === 0) {
    return (
      <div className="py-10 text-center text-xs text-slate-500">
        Không có bệnh nhân phù hợp bộ lọc hiện tại.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-200">
            <th className="py-2 pr-3">Số TT</th>
            <th className="py-2 pr-3">Bệnh nhân</th>
            <th className="py-2 pr-3">Lý do khám / Triệu chứng</th>
            <th className="py-2 pr-3">Phân công</th>
            <th className="py-2 pr-3">Phân loại</th>
            <th className="py-2">Trạng thái</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => {
            const priorityBadge = PRIORITY_META[entry.priority];
            return (
              <tr
                key={entry.id}
                onClick={() => onSelectPatient?.(entry)}
                className="border-b border-slate-100 last:border-0 hover:bg-slate-50 cursor-pointer transition-colors"
              >
                <td className="py-2.5 pr-3 font-mono font-bold text-slate-800">{entry.queueNo}</td>
                <td className="py-2.5 pr-3">
                  <div className="font-semibold text-slate-800">{entry.patientName}</div>
                  <div className="text-[10px] text-slate-400 font-mono">
                    {entry.patientCode} • {entry.gender}, {entry.age}t
                  </div>
                </td>
                <td className="py-2.5 pr-3 max-w-xs">
                  <p className="text-slate-600 line-clamp-1" title={entry.chiefComplaint}>
                    {entry.chiefComplaint}
                  </p>
                </td>
                <td className="py-2.5 pr-3 tabular-nums text-slate-600 font-mono">
                  {entry.assignedAt}
                </td>
                <td className="py-2.5 pr-3">
                  {priorityBadge ? (
                    <Badge tone={priorityBadge.tone}>{priorityBadge.label}</Badge>
                  ) : (
                    <span className="text-[11px] text-slate-400">Thường quy</span>
                  )}
                </td>
                <td className="py-2.5">
                  <Badge tone={STATUS_META[entry.status].tone}>
                    {STATUS_META[entry.status].label}
                  </Badge>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
