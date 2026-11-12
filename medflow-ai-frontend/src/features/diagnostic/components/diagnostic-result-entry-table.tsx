import {
  AlertCircle,
  CheckCircle2,
  Eye,
  FileSignature,
  Layers,
  PencilLine,
  ShieldAlert,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  DiagnosticCategory,
  DiagnosticOrderStatus,
  DiagnosticPriority,
  DiagnosticQueueStatus,
  DiagnosticResultEntryItemDto,
  DiagnosticResultStatus,
} from '../types/diagnostic.types';

interface DiagnosticResultEntryTableProps {
  items: DiagnosticResultEntryItemDto[];
  selectedOrderCode?: string | null;
  onSelectOrder: (order: DiagnosticResultEntryItemDto) => void;
}

const STATUS_META: Record<
  DiagnosticOrderStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  ORDERED: { label: 'Chờ xác nhận', tone: 'amber' },
  CONFIRMED: { label: 'Đã xác nhận', tone: 'sky' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'emerald' },
  COMPLETED: { label: 'Đã hoàn tất', tone: 'slate' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
};

const QUEUE_STATUS_META: Record<
  DiagnosticQueueStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  RESERVED: { label: 'Giữ chỗ', tone: 'amber' },
  WAITING: { label: 'Chờ tiếp nhận', tone: 'amber' },
  READY: { label: 'Sẵn sàng', tone: 'emerald' },
  CALLED: { label: 'Đã gọi vào phòng', tone: 'sky' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'sky' },
  SKIPPED: { label: 'Qua lượt', tone: 'rose' },
  COMPLETED: { label: 'Hoàn tất', tone: 'slate' },
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
  STAT: { label: 'Cấp cứu', tone: 'rose' },
};

const RESULT_STATUS_META: Record<
  DiagnosticResultStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  NONE: { label: 'Chưa có KQ', tone: 'amber' },
  DRAFT: { label: 'Bản nháp', tone: 'sky' },
  PRELIMINARY: { label: 'Kết quả sơ bộ', tone: 'sky' },
  PENDING_VERIFY: { label: 'Chờ thẩm định', tone: 'amber' },
  FINALIZED: { label: 'Đã duyệt', tone: 'emerald' },
  VERIFIED: { label: 'Đã thẩm định', tone: 'emerald' },
};

function formatOrderTime(isoOrText?: string | null): string {
  if (!isoOrText) return '--:--';
  const parsed = new Date(isoOrText);
  if (Number.isNaN(parsed.getTime())) return isoOrText;
  return parsed.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

function formatGenderLabel(gender?: string | null): string {
  if (gender === 'MALE') return 'Nam';
  if (gender === 'FEMALE') return 'Nữ';
  return 'Chưa rõ';
}

export function DiagnosticResultEntryTable({
  items,
  selectedOrderCode,
  onSelectOrder,
}: DiagnosticResultEntryTableProps) {
  if (items.length === 0) {
    return (
      <div
        className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-canvas/60 px-6 py-10 text-center"
        data-testid="diagnostic-result-entry-empty-state"
      >
        <AlertCircle className="mb-2 h-6 w-6 text-sub" aria-hidden="true" />
        <p className="text-xs font-semibold text-navy">
          Không tìm thấy phiếu chỉ định cận lâm sàng phù hợp với bộ lọc nhập kết quả hiện tại.
        </p>
        <p className="mt-1 text-[11px] text-sub">
          Hãy thử đổi từ khóa tìm kiếm, nhóm kỹ thuật hoặc bộ lọc trạng thái kết quả.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table
        className="w-full text-left border-collapse"
        data-testid="diagnostic-result-entry-table"
      >
        <thead>
          <tr className="border-b border-border text-[11px] font-bold uppercase tracking-wider text-sub">
            <th className="py-2.5 pr-3">Số TT / Mã CĐ</th>
            <th className="py-2.5 pr-3">Bệnh nhân</th>
            <th className="py-2.5 pr-3">Dịch vụ &amp; Nhóm CLS</th>
            <th className="py-2.5 pr-3">Mức ưu tiên</th>
            <th className="py-2.5 pr-3">Trạng thái &amp; KQ</th>
            <th className="py-2.5 pr-3">Giờ nhập / duyệt</th>
            <th className="py-2.5 text-right">Thao tác</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border text-xs">
          {items.map((item) => {
            const isSelected = selectedOrderCode === item.orderCode;

            const statusKey = (item.orderStatus?.toUpperCase() ||
              item.status?.toUpperCase() ||
              'IN_PROGRESS') as DiagnosticOrderStatus;
            const statusMeta = STATUS_META[statusKey] ?? {
              label: item.orderStatus || item.status,
              tone: 'slate' as const,
            };

            const queueStatusKey = (item.queueStatus?.toUpperCase() ||
              'WAITING') as DiagnosticQueueStatus;
            const queueStatusMeta = QUEUE_STATUS_META[queueStatusKey] ?? {
              label: item.queueStatus ?? 'WAITING',
              tone: 'slate' as const,
            };

            const categoryKey = (item.category?.toUpperCase() ||
              'LABORATORY') as DiagnosticCategory;
            const categoryMeta = CATEGORY_META[categoryKey] ?? {
              label: item.category,
              tone: 'sky' as const,
            };

            const priorityKey = (item.priority?.toUpperCase() || 'NORMAL') as DiagnosticPriority;
            const priorityMeta = PRIORITY_META[priorityKey] ?? {
              label: item.priority,
              tone: 'slate' as const,
            };

            const resultStatusKey = (item.resultStatus?.toUpperCase() ||
              'NONE') as DiagnosticResultStatus;
            const resultStatusMeta = RESULT_STATUS_META[resultStatusKey] ?? {
              label: item.resultStatus,
              tone: 'slate' as const,
            };

            return (
              <tr
                key={item.orderCode}
                data-testid={`result-entry-order-row-${item.orderCode}`}
                onClick={() => onSelectOrder(item)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/90' : 'hover:bg-canvas'
                }`}
              >
                <td className="py-3 pr-3 align-top">
                  <div className="flex items-center gap-1.5">
                    <span className="font-mono text-xs font-bold text-primary">
                      {item.ticketNo ?? '--'}
                    </span>
                    <span className="text-[10px] text-sub">
                      • {formatOrderTime(item.startedAt ?? item.confirmedAt ?? item.orderedAt)}
                    </span>
                  </div>
                  <div className="font-mono text-[11px] font-semibold text-navy">
                    {item.orderCode}
                  </div>
                  {item.isAdditionalOrder && (
                    <div className="mt-1 inline-flex items-center gap-1 rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-800">
                      <Layers className="h-3 w-3" aria-hidden="true" />
                      <span>Chỉ định bổ sung #{item.orderSequenceNo}</span>
                    </div>
                  )}
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="font-semibold text-ink">{item.patient.fullName}</div>
                  <div className="text-[11px] text-sub">
                    <span className="font-mono">{item.patient.patientCode}</span> •{' '}
                    {formatGenderLabel(item.patient.gender)}
                    {item.patient.age ? `, ${item.patient.age}T` : ''}
                  </div>
                  {item.patient.allergies && (
                    <div className="mt-1 inline-flex items-center gap-1 text-[10px] font-medium text-rose-700">
                      <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
                      <span className="truncate max-w-[170px]" title={item.patient.allergies}>
                        Lưu ý: {item.patient.allergies}
                      </span>
                    </div>
                  )}
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="font-medium text-ink">{item.serviceName}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <Badge tone={categoryMeta.tone}>{categoryMeta.label}</Badge>
                    <span className="font-mono text-[10px] text-sub">{item.serviceCode}</span>
                    {item.requiresResultVerification && (
                      <span className="rounded border border-sky-200 bg-sky-50 px-1.5 py-0.5 text-[10px] font-semibold text-sky-700">
                        Bắt buộc thẩm định
                      </span>
                    )}
                  </div>
                  <div className="mt-1 text-[10px] text-sub">
                    BS: {item.orderingDoctorName} •{' '}
                    <span className="font-mono">{item.visitCode}</span>
                  </div>
                </td>

                <td className="py-3 pr-3 align-top">
                  <Badge tone={priorityMeta.tone}>{priorityMeta.label}</Badge>
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="flex flex-col items-start gap-1">
                    <Badge tone={resultStatusMeta.tone}>{resultStatusMeta.label}</Badge>
                    <span className="text-[10px] text-sub">
                      {statusMeta.label} • {queueStatusMeta.label}
                    </span>
                    {item.latestVersionNo != null && item.latestVersionNo > 0 && (
                      <span className="font-mono text-[10px] text-sub">
                        v{item.latestVersionNo}
                      </span>
                    )}
                    {item.canEnterResult ? (
                      <span className="inline-flex items-center gap-1 text-[10px] font-medium text-emerald-700">
                        <CheckCircle2 className="h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>Sẵn sàng nhập KQ</span>
                      </span>
                    ) : item.canAmendResult ? (
                      <span className="inline-flex items-center gap-1 text-[10px] font-medium text-emerald-700">
                        <CheckCircle2 className="h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>Có thể sửa KQ đã khóa</span>
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-sub">
                        <AlertCircle className="h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>{item.isResultFinalized ? 'Đã duyệt KQ' : 'Không thể nhập KQ'}</span>
                      </span>
                    )}
                  </div>
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="font-mono text-[11px] text-ink">
                    {formatOrderTime(item.enteredAt)}
                  </div>
                  <div className="mt-0.5 font-mono text-[10px] text-sub">
                    {item.resultFinalizedAt
                      ? `Duyệt: ${formatOrderTime(item.resultFinalizedAt)}`
                      : (item.enteredByStaffName ?? 'Chưa nhập')}
                  </div>
                </td>

                <td
                  className="py-3 text-right align-top"
                  onClick={(e) => {
                    e.stopPropagation();
                  }}
                >
                  <button
                    type="button"
                    onClick={() => onSelectOrder(item)}
                    className={`inline-flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                      item.canEnterResult || item.canAmendResult
                        ? 'border-primary bg-primary text-white hover:bg-primary-dark'
                        : 'border-border bg-white text-navy hover:bg-canvas'
                    }`}
                  >
                    {item.canEnterResult ? (
                      <>
                        <FileSignature className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Nhập KQ</span>
                      </>
                    ) : item.canAmendResult ? (
                      <>
                        <PencilLine className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Sửa KQ</span>
                      </>
                    ) : (
                      <>
                        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Xem chi tiết</span>
                      </>
                    )}
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
