import { AlertTriangle, CheckCircle2, Eye, Play, UserCheck, FileText } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  DiagnosticWorklistActionId,
  DiagnosticWorklistItemDto,
} from '../types/diagnostic-worklist.types';

interface DiagnosticWorklistTableProps {
  items: DiagnosticWorklistItemDto[];
  canConfirmOrders: boolean;
  canManageQueue: boolean;
  canWriteResults: boolean;
  selectedOrderCode?: string | null;
  onSelectOrder: (item: DiagnosticWorklistItemDto) => void;
  onTriggerAction: (item: DiagnosticWorklistItemDto, actionId: DiagnosticWorklistActionId) => void;
}

const PRIORITY_TONE: Record<DiagnosticWorklistItemDto['priority'], 'rose' | 'amber' | 'slate'> = {
  STAT: 'rose',
  URGENT: 'amber',
  NORMAL: 'slate',
};

const STATUS_TONE: Record<
  DiagnosticWorklistItemDto['status'],
  'amber' | 'sky' | 'emerald' | 'slate' | 'rose'
> = {
  ORDERED: 'amber',
  CONFIRMED: 'sky',
  IN_PROGRESS: 'emerald',
  COMPLETED: 'slate',
  CANCELLED: 'rose',
};

const CATEGORY_TONE: Record<DiagnosticWorklistItemDto['category'], 'sky' | 'amber' | 'emerald'> = {
  LABORATORY: 'sky',
  IMAGING: 'amber',
  FUNCTIONAL: 'emerald',
};

const QUEUE_STATUS_LABEL: Record<string, string> = {
  WAITING: 'Đang chờ gọi',
  READY: 'Sẵn sàng vào phòng',
  CALLED: 'Đã gọi vào phòng',
  IN_PROGRESS: 'Đang thực hiện',
  COMPLETED: 'Đã hoàn tất',
  SKIPPED: 'Tạm qua lượt',
  CANCELLED: 'Đã hủy lượt',
};

const RESULT_STATUS_BADGE: Record<
  string,
  { label: string; tone: 'slate' | 'amber' | 'sky' | 'emerald' | 'rose' }
> = {
  PENDING: { label: 'Chưa có KQ', tone: 'slate' },
  DRAFT: { label: 'KQ nháp', tone: 'amber' },
  PRELIMINARY: { label: 'KQ sơ bộ', tone: 'sky' },
  PENDING_VERIFY: { label: 'Chờ duyệt KQ', tone: 'sky' },
  FINALIZED: { label: 'Đã trả KQ', tone: 'emerald' },
  VERIFIED: { label: 'Đã ký duyệt', tone: 'emerald' },
  CANCELLED: { label: 'Hủy KQ', tone: 'rose' },
};

export function DiagnosticWorklistTable({
  items,
  canConfirmOrders,
  canManageQueue,
  canWriteResults,
  selectedOrderCode,
  onSelectOrder,
  onTriggerAction,
}: DiagnosticWorklistTableProps) {
  if (items.length === 0) {
    return (
      <div
        role="status"
        className="rounded-xl border border-dashed border-border bg-canvas px-4 py-10 text-center text-xs text-sub"
      >
        Không tìm thấy chỉ định cận lâm sàng phù hợp với bộ lọc hiện tại.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table
        aria-label="Danh sách công việc cận lâm sàng"
        className="w-full text-left border-collapse"
      >
        <thead>
          <tr className="border-b border-border text-[11px] font-bold uppercase tracking-wider text-sub">
            <th className="py-2.5 pr-3">Số phiếu / Mã CĐ</th>
            <th className="py-2.5 px-3">Bệnh nhân</th>
            <th className="py-2.5 px-3">Dịch vụ cận lâm sàng</th>
            <th className="py-2.5 px-3">Điểm thực hiện</th>
            <th className="py-2.5 px-3">BS chỉ định</th>
            <th className="py-2.5 px-3">Ưu tiên</th>
            <th className="py-2.5 px-3">Trạng thái</th>
            <th className="py-2.5 pl-3 text-right">Thao tác</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border text-xs">
          {items.map((item) => {
            const isSelected = selectedOrderCode === item.orderCode;
            const resultBadge = RESULT_STATUS_BADGE[item.resultStatus] ?? {
              label: item.resultStatus,
              tone: 'slate' as const,
            };

            const showConfirmBtn = canConfirmOrders && item.canConfirmOrder;
            const showReceiveBtn = canManageQueue && item.canReceivePatient;
            const showExecutionBtn =
              (canManageQueue || canWriteResults) && item.canUpdateExecutionStatus;
            const showEnterResultBtn = canWriteResults && item.canEnterResult;
            const showViewResultBtn = item.canViewResult;

            return (
              <tr
                key={item.id}
                data-testid={`worklist-row-${item.orderCode}`}
                className={`transition-colors hover:bg-canvas/80 ${
                  isSelected ? 'bg-sky-50/60' : ''
                }`}
              >
                <td className="py-3 pr-3 align-top">
                  <div className="flex items-center gap-1.5">
                    <span className="font-mono text-xs font-bold text-navy">{item.ticketNo}</span>
                    {item.requiredForConclusion && (
                      <Badge tone="sky" className="text-[10px]">
                        Bắt buộc KL
                      </Badge>
                    )}
                  </div>
                  <div className="mt-0.5 font-mono text-[11px] font-semibold text-primary">
                    {item.orderCode}
                  </div>
                  <div className="font-mono text-[10px] text-sub">Lượt: {item.visitCode}</div>
                  {item.isAdditionalOrder && (
                    <div className="mt-1">
                      <Badge tone="amber" className="text-[10px]">
                        Bổ sung #{item.orderSequenceNo}
                      </Badge>
                    </div>
                  )}
                </td>

                <td className="py-3 px-3 align-top">
                  <div className="font-semibold text-ink">{item.patientName}</div>
                  <div className="text-[11px] text-sub">
                    {item.patientGenderLabel}
                    {typeof item.patientAge === 'number' ? ` • ${item.patientAge} tuổi` : ''}
                  </div>
                  <div className="font-mono text-[10px] text-sub">{item.patientCode}</div>
                  {item.allergiesSummary && (
                    <div className="mt-1 inline-flex items-center gap-1 rounded-md border border-rose-200 bg-rose-50 px-1.5 py-0.5 text-[10px] font-semibold text-rose-700">
                      <AlertTriangle className="h-3 w-3 shrink-0" aria-hidden="true" />
                      <span className="truncate max-w-[160px]" title={item.allergiesSummary}>
                        {item.allergiesSummary}
                      </span>
                    </div>
                  )}
                </td>

                <td className="py-3 px-3 align-top">
                  <div className="font-medium text-ink">{item.serviceName}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <Badge tone={CATEGORY_TONE[item.category]}>{item.categoryLabel}</Badge>
                    <span className="font-mono text-[10px] text-sub">{item.serviceCode}</span>
                  </div>
                  {item.instruction && (
                    <div
                      className="mt-1 line-clamp-1 max-w-[240px] text-[10px] text-sub"
                      title={item.instruction}
                    >
                      HD: {item.instruction}
                    </div>
                  )}
                </td>

                <td className="py-3 px-3 align-top">
                  <div className="font-mono text-xs font-semibold text-navy">{item.room}</div>
                  {item.servicePointName && (
                    <div className="max-w-[170px] truncate text-[10px] text-sub">
                      {item.servicePointName}
                    </div>
                  )}
                  {item.queueStatus && (
                    <div className="mt-1 text-[10px] font-medium text-sky-700">
                      {QUEUE_STATUS_LABEL[item.queueStatus] ?? item.queueStatus}
                    </div>
                  )}
                </td>

                <td className="py-3 px-3 align-top">
                  <div className="font-medium text-ink">{item.orderingDoctor}</div>
                  <div className="font-mono text-[10px] text-sub">Giờ CĐ: {item.orderedAt}</div>
                  {item.reasonForVisit && (
                    <div
                      className="mt-0.5 line-clamp-1 max-w-[160px] text-[10px] text-sub"
                      title={item.reasonForVisit}
                    >
                      {item.reasonForVisit}
                    </div>
                  )}
                </td>

                <td className="py-3 px-3 align-top">
                  <Badge tone={PRIORITY_TONE[item.priority]}>{item.priorityLabel}</Badge>
                </td>

                <td className="py-3 px-3 align-top">
                  <div className="flex flex-col items-start gap-1">
                    <Badge tone={STATUS_TONE[item.status]}>{item.statusLabel}</Badge>
                    <Badge tone={resultBadge.tone} className="text-[10px]">
                      {resultBadge.label}
                    </Badge>
                  </div>
                </td>

                <td className="py-3 pl-3 align-top text-right">
                  <div className="flex flex-wrap items-center justify-end gap-1.5">
                    <button
                      type="button"
                      onClick={() => onSelectOrder(item)}
                      aria-label={`Xem chi tiết ${item.orderCode}`}
                      className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-navy transition-colors hover:bg-canvas"
                    >
                      <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Xem chi tiết</span>
                    </button>

                    {showConfirmBtn && (
                      <button
                        type="button"
                        onClick={() => onTriggerAction(item, 'confirm-order')}
                        aria-label={`Xác nhận chỉ định ${item.orderCode}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-primary px-2.5 py-1 text-[11px] font-semibold text-white transition-colors hover:bg-primary-dark"
                      >
                        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Xác nhận CĐ</span>
                      </button>
                    )}

                    {showReceiveBtn && (
                      <button
                        type="button"
                        onClick={() => onTriggerAction(item, 'receive-patient')}
                        aria-label={`Tiếp nhận bệnh nhân ${item.orderCode}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-[11px] font-semibold text-emerald-700 transition-colors hover:bg-emerald-100"
                      >
                        <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Tiếp nhận BN</span>
                      </button>
                    )}

                    {showExecutionBtn && (
                      <button
                        type="button"
                        onClick={() => onTriggerAction(item, 'update-execution')}
                        aria-label={`Cập nhật thực hiện ${item.orderCode}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1 text-[11px] font-semibold text-amber-800 transition-colors hover:bg-amber-100"
                      >
                        <Play className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Thực hiện CLS</span>
                      </button>
                    )}

                    {showEnterResultBtn && (
                      <button
                        type="button"
                        onClick={() => onTriggerAction(item, 'enter-result')}
                        aria-label={`Nhập kết quả ${item.orderCode}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-sky-200 bg-sky-50 px-2.5 py-1 text-[11px] font-semibold text-sky-700 transition-colors hover:bg-sky-100"
                      >
                        <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Nhập & trả KQ</span>
                      </button>
                    )}

                    {showViewResultBtn && (
                      <button
                        type="button"
                        onClick={() => onTriggerAction(item, 'view-result')}
                        aria-label={`Xem kết quả CLS ${item.orderCode}`}
                        className="inline-flex items-center gap-1 rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-[11px] font-semibold text-emerald-700 transition-colors hover:bg-emerald-100"
                      >
                        <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Xem kết quả CLS</span>
                      </button>
                    )}
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
