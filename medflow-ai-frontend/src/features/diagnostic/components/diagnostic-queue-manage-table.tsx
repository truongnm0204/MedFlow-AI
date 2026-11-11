import {
  AlertCircle,
  CheckCircle2,
  Eye,
  Megaphone,
  Play,
  RotateCcw,
  ShieldAlert,
  SkipForward,
  UserCheck,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { DiagnosticCategory, DiagnosticQueueEntryItemDto } from '../types/diagnostic.types';

export type QuickQueueRowAction =
  'ready' | 'call' | 'start' | 'complete' | 'missed-turn' | 'restore-turn';

interface DiagnosticQueueManageTableProps {
  entries: DiagnosticQueueEntryItemDto[];
  selectedEntryId?: string | null;
  hasManagePermission: boolean;
  isMutating?: boolean;
  onSelectEntry: (entry: DiagnosticQueueEntryItemDto) => void;
  onQuickAction: (entry: DiagnosticQueueEntryItemDto, action: QuickQueueRowAction) => void;
}

const QUEUE_STATUS_META: Record<
  string,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  RESERVED: { label: 'Đã giữ chỗ', tone: 'slate' },
  WAITING: { label: 'Chờ tiếp nhận', tone: 'amber' },
  READY: { label: 'Sẵn sàng gọi', tone: 'sky' },
  CALLED: { label: 'Đã gọi số', tone: 'amber' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'emerald' },
  COMPLETED: { label: 'Đã hoàn tất', tone: 'slate' },
  SKIPPED: { label: 'Qua lượt', tone: 'rose' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
};

const PRESENCE_META: Record<
  string,
  { label: string; tone: 'emerald' | 'amber' | 'rose' | 'slate' }
> = {
  PRESENT: { label: 'Có mặt', tone: 'emerald' },
  NOT_ARRIVED: { label: 'Chưa đến', tone: 'amber' },
  LEFT: { label: 'Tạm rời khu CLS', tone: 'rose' },
};

const PRIORITY_META: Record<string, { label: string; tone: 'slate' | 'amber' | 'rose' | 'sky' }> = {
  NORMAL: { label: 'Bình thường', tone: 'slate' },
  URGENT: { label: 'Khẩn (URGENT)', tone: 'amber' },
  STAT: { label: 'Cấp cứu STAT', tone: 'rose' },
  ELDERLY: { label: 'Cao tuổi', tone: 'sky' },
  PREGNANT: { label: 'Thai phụ', tone: 'sky' },
};

const CATEGORY_META: Record<
  DiagnosticCategory,
  { label: string; tone: 'sky' | 'emerald' | 'amber' }
> = {
  LABORATORY: { label: 'Xét nghiệm', tone: 'sky' },
  IMAGING: { label: 'CĐ hình ảnh', tone: 'emerald' },
  FUNCTIONAL: { label: 'Thăm dò CN', tone: 'amber' },
};

export function DiagnosticQueueManageTable({
  entries,
  selectedEntryId,
  hasManagePermission,
  isMutating = false,
  onSelectEntry,
  onQuickAction,
}: DiagnosticQueueManageTableProps) {
  if (entries.length === 0) {
    return (
      <div
        className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-canvas/60 px-6 py-10 text-center"
        data-testid="diagnostic-queue-empty-state"
      >
        <AlertCircle className="mb-2 h-6 w-6 text-sub" aria-hidden="true" />
        <p className="text-xs font-semibold text-navy">
          Không tìm thấy lượt xếp hàng cận lâm sàng phù hợp với bộ lọc hiện tại.
        </p>
        <p className="mt-1 text-[11px] text-sub">
          Hãy thử đổi từ khóa tìm kiếm, điểm phục vụ hoặc đặt lại bộ lọc.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table
        className="w-full border-collapse text-left"
        aria-label="Danh sách hàng đợi thực hiện cận lâm sàng"
        data-testid="diagnostic-queue-manage-table"
      >
        <thead>
          <tr className="border-b border-border text-[11px] font-bold uppercase tracking-wider text-sub">
            <th className="py-2.5 pr-3">Số TT / Lượt chờ</th>
            <th className="py-2.5 pr-3">Bệnh nhân</th>
            <th className="py-2.5 pr-3">Dịch vụ & Điểm phục vụ</th>
            <th className="py-2.5 pr-3">Ưu tiên</th>
            <th className="py-2.5 pr-3">Trạng thái & Hiện diện</th>
            <th className="py-2.5 text-right">Thao tác điều phối</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border text-xs">
          {entries.map((entry) => {
            const isSelected =
              Boolean(selectedEntryId) &&
              (entry.formattedQueueEntryId.toUpperCase() === selectedEntryId!.toUpperCase() ||
                entry.id.toUpperCase() === selectedEntryId!.toUpperCase() ||
                entry.ticketNo.toUpperCase() === selectedEntryId!.toUpperCase());

            const statusMeta = QUEUE_STATUS_META[entry.status] ?? {
              label: entry.statusLabel || entry.status,
              tone: 'slate' as const,
            };
            const presenceMeta = PRESENCE_META[entry.presenceStatus] ?? {
              label: entry.presenceStatusLabel || entry.presenceStatus,
              tone: 'slate' as const,
            };
            const priorityMeta = PRIORITY_META[entry.clinicalPriority] ??
              PRIORITY_META[entry.priority] ?? {
                label: entry.priorityLabel || entry.priority,
                tone: 'slate' as const,
              };
            const categoryMeta = CATEGORY_META[entry.category] ?? {
              label: entry.categoryLabel || entry.category,
              tone: 'sky' as const,
            };

            return (
              <tr
                key={entry.formattedQueueEntryId}
                data-testid={`queue-entry-row-${entry.formattedQueueEntryId}`}
                onClick={() => onSelectEntry(entry)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/90' : 'hover:bg-canvas/80'
                }`}
              >
                {/* Ticket & Queue ID */}
                <td className="py-3 pr-3 align-top">
                  <div className="flex items-center gap-1.5">
                    <span className="inline-flex h-6 min-w-12 items-center justify-center rounded-md bg-navy px-2 font-mono text-xs font-bold text-white">
                      {entry.ticketNo}
                    </span>
                    <span className="font-mono text-[10px] font-semibold text-sub">
                      #{entry.servingOrder}
                    </span>
                  </div>
                  <div className="mt-1 font-mono text-[10px] text-sub">
                    {entry.formattedQueueEntryId}
                  </div>
                  {entry.orderCode && (
                    <div className="font-mono text-[10px] font-medium text-primary">
                      {entry.orderCode}
                    </div>
                  )}
                </td>

                {/* Patient */}
                <td className="py-3 pr-3 align-top">
                  <div className="font-bold text-ink">{entry.patientName}</div>
                  <div className="mt-0.5 font-mono text-[10px] text-sub">
                    {entry.patientCode} • {entry.patientGenderLabel}
                    {entry.patientAge ? ` • ${entry.patientAge} tuổi` : ''}
                  </div>
                  {entry.allergiesSummary && (
                    <div className="mt-1 inline-flex items-center gap-1 rounded bg-rose-50 px-1.5 py-0.5 text-[10px] font-semibold text-rose-700">
                      <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
                      <span className="truncate max-w-[160px]">{entry.allergiesSummary}</span>
                    </div>
                  )}
                </td>

                {/* Service & Room */}
                <td className="py-3 pr-3 align-top">
                  <div className="font-semibold text-navy line-clamp-1">{entry.serviceName}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <Badge tone={categoryMeta.tone}>{categoryMeta.label}</Badge>
                    <span className="font-mono text-[11px] font-semibold text-ink">
                      {entry.roomCode}
                    </span>
                    <span className="text-[10px] text-sub">• {entry.servicePointName}</span>
                  </div>
                  <div className="mt-0.5 text-[10px] text-sub">
                    BS chỉ định: {entry.orderingDoctorName}
                  </div>
                </td>

                {/* Priority */}
                <td className="py-3 pr-3 align-top">
                  <div className="flex flex-col items-start gap-1">
                    <Badge tone={priorityMeta.tone}>{priorityMeta.label}</Badge>
                    {entry.requiredForConclusion && (
                      <span className="rounded bg-amber-100/80 px-1.5 py-0.5 text-[10px] font-semibold text-amber-900">
                        Bắt buộc KL
                      </span>
                    )}
                  </div>
                </td>

                {/* Queue Status & Presence */}
                <td className="py-3 pr-3 align-top">
                  <div className="flex flex-wrap items-center gap-1">
                    <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                    <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
                  </div>
                  <div className="mt-1 flex items-center gap-2 text-[10px] text-sub">
                    <span>{entry.estimatedWaitLabel}</span>
                    <span className="font-mono">• v{entry.stateVersion}</span>
                  </div>
                  {entry.blockedReason && (
                    <div className="mt-1 text-[10px] font-medium text-rose-700 line-clamp-1">
                      Lý do: {entry.blockedReason}
                    </div>
                  )}
                </td>

                {/* Actions */}
                <td className="py-3 text-right align-top" onClick={(e) => e.stopPropagation()}>
                  <div className="flex flex-wrap items-center justify-end gap-1">
                    {hasManagePermission && entry.status === 'WAITING' && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'ready')}
                        data-testid={`row-action-ready-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-sky-200 bg-sky-50 px-2 py-1 text-[11px] font-semibold text-sky-700 transition-colors hover:bg-sky-100 disabled:opacity-50"
                      >
                        <UserCheck className="h-3 w-3" aria-hidden="true" />
                        <span>Sẵn sàng</span>
                      </button>
                    )}

                    {hasManagePermission && entry.canCall && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'call')}
                        data-testid={`row-action-call-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-primary px-2 py-1 text-[11px] font-semibold text-white transition-colors hover:bg-primary-dark disabled:opacity-50"
                      >
                        <Megaphone className="h-3 w-3" aria-hidden="true" />
                        <span>Gọi BN</span>
                      </button>
                    )}

                    {hasManagePermission && entry.canStart && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'start')}
                        data-testid={`row-action-start-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2 py-1 text-[11px] font-semibold text-white transition-colors hover:bg-emerald-700 disabled:opacity-50"
                      >
                        <Play className="h-3 w-3" aria-hidden="true" />
                        <span>Bắt đầu</span>
                      </button>
                    )}

                    {hasManagePermission && entry.canComplete && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'complete')}
                        data-testid={`row-action-complete-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2 py-1 text-[11px] font-semibold text-white transition-colors hover:bg-emerald-700 disabled:opacity-50"
                      >
                        <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                        <span>Hoàn tất</span>
                      </button>
                    )}

                    {hasManagePermission && entry.canRestore && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'restore-turn')}
                        data-testid={`row-action-restore-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50 px-2 py-1 text-[11px] font-semibold text-emerald-700 transition-colors hover:bg-emerald-100 disabled:opacity-50"
                      >
                        <RotateCcw className="h-3 w-3" aria-hidden="true" />
                        <span>Khôi phục</span>
                      </button>
                    )}

                    {hasManagePermission && entry.canSkip && entry.status === 'CALLED' && (
                      <button
                        type="button"
                        disabled={isMutating}
                        onClick={() => onQuickAction(entry, 'missed-turn')}
                        data-testid={`row-action-skip-${entry.formattedQueueEntryId}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-amber-200 bg-amber-50 px-2 py-1 text-[11px] font-semibold text-amber-800 transition-colors hover:bg-amber-100 disabled:opacity-50"
                      >
                        <SkipForward className="h-3 w-3" aria-hidden="true" />
                        <span>Qua lượt</span>
                      </button>
                    )}

                    <button
                      type="button"
                      onClick={() => onSelectEntry(entry)}
                      aria-label={`Xem chi tiết lượt xếp hàng ${entry.ticketNo}`}
                      data-testid={`row-action-detail-${entry.formattedQueueEntryId}`}
                      className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2 py-1 text-[11px] font-semibold text-navy transition-colors hover:bg-canvas"
                    >
                      <Eye className="h-3 w-3" aria-hidden="true" />
                      <span>Chi tiết</span>
                    </button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
