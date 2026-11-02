import { ArrowRight, History } from 'lucide-react';

import type { TransferHistoryEntry } from '../types/transfer-room.types';

interface TransferHistoryListProps {
  entries: TransferHistoryEntry[];
}

export function TransferHistoryList({ entries }: TransferHistoryListProps) {
  if (entries.length === 0) {
    return <p className="text-sm text-sub">Chưa có lần điều phối nào trước đó.</p>;
  }

  return (
    <ul className="space-y-3" aria-label="Lịch sử điều phối">
      {entries.map((e) => (
        <li key={e.id} className="rounded-xl border border-border bg-canvas px-4 py-3 text-xs">
          <div className="flex flex-wrap items-center gap-2 font-semibold text-navy">
            <History className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>{e.fromRoom}</span>
            <ArrowRight className="h-3 w-3" aria-hidden="true" />
            <span>{e.toRoom}</span>
            <span className="ml-auto font-normal tabular-nums text-sub">{e.performedAt}</span>
          </div>
          <div className="mt-1 text-ink">Lý do: {e.reason}</div>
          {e.note && <div className="mt-0.5 text-sub">Ghi chú: {e.note}</div>}
          <div className="mt-0.5 text-sub">Thực hiện bởi: {e.performedBy}</div>
        </li>
      ))}
    </ul>
  );
}
