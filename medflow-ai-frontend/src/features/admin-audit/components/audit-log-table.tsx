import { ChevronLeft, ChevronRight, Code2, Eye, RotateCcw } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { formatHospitalDateTimeDisplay } from '../schemas/audit-log.schema';
import type { AuditLogCategory, AuditLogListItemDto } from '../types/audit-log.types';

interface AuditLogTableProps {
  items: AuditLogListItemDto[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  selectedLogId: number | null;
  onSelectLog: (item: AuditLogListItemDto) => void;
  onPageChange: (nextPage: number) => void;
  onPageSizeChange: (nextPageSize: number) => void;
  onResetFilters: () => void;
}

const CATEGORY_BADGES: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' }
> = {
  AUTH: { label: 'Xác thực (AUTH)', tone: 'sky' },
  RBAC: { label: 'Phân quyền (RBAC)', tone: 'emerald' },
  CONFIG: { label: 'Cấu hình (CONFIG)', tone: 'amber' },
  OPERATIONS: { label: 'Vận hành', tone: 'slate' },
};

function resolveCategoryBadge(category: AuditLogCategory) {
  return CATEGORY_BADGES[category] ?? { label: category, tone: 'slate' as const };
}

export function AuditLogTable({
  items,
  total,
  page,
  pageSize,
  totalPages,
  selectedLogId,
  onSelectLog,
  onPageChange,
  onPageSizeChange,
  onResetFilters,
}: AuditLogTableProps) {
  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-border bg-white px-6 py-14 text-center">
        <p className="text-sm font-semibold text-ink">
          Không tìm thấy bản ghi nhật ký kiểm toán phù hợp
        </p>
        <p className="max-w-md text-xs text-sub">
          Không có sự kiện nào khớp với tiêu chí lọc hiện tại trong phạm vi phân quyền của bạn.
        </p>
        <Button type="button" variant="secondary" size="sm" onClick={onResetFilters}>
          <RotateCcw className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
          <span>Đặt lại bộ lọc</span>
        </Button>
      </div>
    );
  }

  const effectiveTotalPages = Math.max(1, totalPages);

  return (
    <section
      aria-label="Danh sách sự kiện nhật ký kiểm toán"
      className="overflow-hidden rounded-2xl border border-border bg-white shadow-sm"
    >
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="border-b border-border bg-canvas font-mono text-[10px] tracking-wider uppercase text-sub">
              <th className="px-4 py-3">Mã sự kiện</th>
              <th className="px-4 py-3">Hành động &amp; Đối tượng</th>
              <th className="px-4 py-3">Phân nhóm</th>
              <th className="px-4 py-3">Người thực hiện &amp; IP</th>
              <th className="px-4 py-3">Thời gian (UTC+7)</th>
              <th className="px-4 py-3 text-right">Metadata</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {items.map((item) => {
              const badge = resolveCategoryBadge(item.category);
              const isSelected = selectedLogId === item.id;
              const actorDisplay =
                item.actorFullName ||
                item.actorEmail ||
                (item.actorUserId ? `UID #${item.actorUserId}` : 'Hệ thống');

              return (
                <tr
                  key={item.id}
                  onClick={() => onSelectLog(item)}
                  className={`cursor-pointer transition-colors ${
                    isSelected ? 'bg-sky-50/70' : 'hover:bg-canvas'
                  }`}
                >
                  <td className="px-4 py-3.5 whitespace-nowrap">
                    <div className="font-mono text-xs font-bold text-primary">{item.auditCode}</div>
                    <div className="mt-0.5 text-[11px] text-sub">
                      {item.hospitalName
                        ? item.hospitalName
                        : item.hospitalId
                          ? `Cơ sở #${item.hospitalId}`
                          : 'Toàn hệ thống'}
                    </div>
                  </td>

                  <td className="px-4 py-3.5">
                    <div className="font-mono text-xs font-bold text-ink">{item.action}</div>
                    <div className="mt-0.5 text-[11px] text-sub">
                      <span className="font-mono font-semibold text-ink">{item.entityType}</span>
                      {item.entityId ? (
                        <span className="font-mono"> • #{item.entityId}</span>
                      ) : null}
                      {item.reason ? <span> — {item.reason}</span> : null}
                    </div>
                  </td>

                  <td className="px-4 py-3.5 whitespace-nowrap">
                    <Badge tone={badge.tone}>{badge.label}</Badge>
                  </td>

                  <td className="px-4 py-3.5">
                    <div className="font-semibold text-ink">{actorDisplay}</div>
                    <div className="mt-0.5 font-mono text-[11px] text-sub">
                      {item.actorEmail ? `${item.actorEmail} • ` : ''}
                      IP: {item.ipAddress ?? 'N/A'}
                    </div>
                  </td>

                  <td className="px-4 py-3.5 font-mono text-[11px] whitespace-nowrap text-ink">
                    <div>
                      {formatHospitalDateTimeDisplay(item.occurredAtHospitalLocal, item.occurredAt)}
                    </div>
                    <div className="text-[10px] text-sub">{item.occurredAt}</div>
                  </td>

                  <td className="px-4 py-3.5 text-right whitespace-nowrap">
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectLog(item);
                      }}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-primary transition-colors hover:border-sky-300 hover:bg-sky-50 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
                    >
                      {item.hasMetadata ? (
                        <Code2 className="h-3.5 w-3.5" aria-hidden="true" />
                      ) : (
                        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                      )}
                      <span>Chi tiết JSON</span>
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="flex flex-col gap-3 border-t border-border bg-canvas px-4 py-3 text-xs text-sub sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <span>
            Tổng cộng <strong className="font-mono text-ink">{total}</strong> sự kiện • Trang{' '}
            <strong className="font-mono text-ink">{page}</strong> /{' '}
            <strong className="font-mono text-ink">{effectiveTotalPages}</strong>
          </span>

          <label className="inline-flex items-center gap-1.5">
            <span>Hiển thị:</span>
            <select
              aria-label="Số dòng mỗi trang"
              value={String(pageSize)}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              className="h-8 rounded-lg border border-border bg-white px-2 font-mono text-xs text-ink focus:border-primary focus:outline-none"
            >
              {[10, 20, 50].map((size) => (
                <option key={size} value={String(size)}>
                  {size} dòng
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          >
            <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Trang trước</span>
          </Button>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={page >= effectiveTotalPages}
            onClick={() => onPageChange(page + 1)}
          >
            <span>Trang tiếp</span>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        </div>
      </div>
    </section>
  );
}
