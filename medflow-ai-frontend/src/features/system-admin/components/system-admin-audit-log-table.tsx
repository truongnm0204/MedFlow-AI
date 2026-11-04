import { RotateCcw } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import { classifyAuditAction } from '../schemas/system-admin.schema';
import type { AuditActionCategory, SystemAdminAuditLogItemDto } from '../types/system-admin.types';

interface SystemAdminAuditLogTableProps {
  logs: SystemAdminAuditLogItemDto[];
  selectedLogId?: number;
  onSelectLog?: (log: SystemAdminAuditLogItemDto) => void;
  onResetFilters?: () => void;
}

const CATEGORY_BADGE_META: Record<
  AuditActionCategory,
  { label: string; tone: 'sky' | 'emerald' | 'amber' }
> = {
  AUTH: { label: 'Xác thực (AUTH)', tone: 'sky' },
  RBAC: { label: 'Phân quyền (RBAC)', tone: 'emerald' },
  CONFIG: { label: 'Cấu hình (CONFIG)', tone: 'amber' },
};

function formatUtcTimestamp(isoString: string): string {
  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) return isoString;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

export function SystemAdminAuditLogTable({
  logs,
  selectedLogId,
  onSelectLog,
  onResetFilters,
}: SystemAdminAuditLogTableProps) {
  if (logs.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-border bg-canvas px-4 py-10 text-center">
        <p className="text-xs font-medium text-sub">
          Không tìm thấy bản ghi nhật ký kiểm toán phù hợp với bộ lọc hiện tại.
        </p>
        {onResetFilters && (
          <button
            type="button"
            onClick={onResetFilters}
            className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-ink shadow-2xs transition-colors hover:bg-slate-50 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <RotateCcw aria-hidden="true" className="h-3.5 w-3.5 text-primary" />
            Đặt lại bộ lọc
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs">
        <thead>
          <tr className="border-b border-border font-mono text-[10px] tracking-wider uppercase text-sub">
            <th className="py-2.5 pr-3">Mã sự kiện</th>
            <th className="py-2.5 pr-3">Hành động &amp; Thực thể</th>
            <th className="py-2.5 pr-3">Phân nhóm</th>
            <th className="py-2.5 pr-3">Tác nhân &amp; IP</th>
            <th className="py-2.5 pr-3">Thời gian (UTC)</th>
            <th className="py-2.5 text-right">Chi tiết</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {logs.map((log) => {
            const category = classifyAuditAction(log.action);
            const categoryMeta = CATEGORY_BADGE_META[category];
            const isSelected = selectedLogId === log.id;

            return (
              <tr
                key={log.id}
                onClick={() => onSelectLog?.(log)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/70' : 'hover:bg-slate-50/80'
                }`}
              >
                <td className="py-3 pr-3 whitespace-nowrap">
                  <div className="font-mono text-xs font-bold text-primary">
                    AUD-{String(log.id).padStart(4, '0')}
                  </div>
                  <div className="font-mono text-[10px] text-sub">
                    {log.hospitalId ? `CS #${log.hospitalId}` : 'Toàn hệ thống'}
                  </div>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-mono text-xs font-bold text-ink">{log.action}</div>
                  <div className="text-[11px] text-sub">
                    {log.entityType}
                    {log.entityId ? ` • ${log.entityId}` : ''}
                    {log.reason ? ` — ${log.reason}` : ''}
                  </div>
                </td>
                <td className="py-3 pr-3 whitespace-nowrap">
                  <Badge tone={categoryMeta.tone}>{categoryMeta.label}</Badge>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-medium text-ink">
                    {log.actorEmail ?? (log.actorUserId ? `UID #${log.actorUserId}` : 'Hệ thống')}
                  </div>
                  <div className="font-mono text-[10px] text-sub">IP: {log.ipAddress ?? 'N/A'}</div>
                </td>
                <td className="py-3 pr-3 font-mono text-[11px] whitespace-nowrap text-sub">
                  {formatUtcTimestamp(log.occurredAt)}
                </td>
                <td className="py-3 text-right whitespace-nowrap">
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      onSelectLog?.(log);
                    }}
                    className="inline-flex items-center rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-primary transition-colors hover:border-sky-300 hover:bg-sky-50"
                  >
                    Xem nhật ký
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
