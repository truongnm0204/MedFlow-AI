import { AlertCircle, CheckCircle2, Eye, Layers, ShieldAlert } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  DiagnosticCategory,
  DiagnosticOrderReviewItemDto,
  DiagnosticOrderStatus,
  DiagnosticPriority,
} from '../types/diagnostic.types';

interface DiagnosticOrderReviewTableProps {
  items: DiagnosticOrderReviewItemDto[];
  selectedOrderCode?: string | null;
  selectedBatchIds: number[];
  onSelectOrder: (order: DiagnosticOrderReviewItemDto) => void;
  onToggleBatchOrder: (orderId: number) => void;
  onToggleAllEligible: (eligibleOrderIds: number[]) => void;
}

const STATUS_META: Record<
  DiagnosticOrderStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  ORDERED: { label: 'Chờ xác nhận', tone: 'amber' },
  CONFIRMED: { label: 'Đã xác nhận', tone: 'sky' },
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

function formatOrderTime(isoOrText: string): string {
  if (!isoOrText) return '--:--';
  if (/^\d{2}:\d{2}$/.test(isoOrText.trim())) return isoOrText.trim();
  const parsed = new Date(isoOrText);
  if (Number.isNaN(parsed.getTime())) return isoOrText;
  return parsed.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

function formatGenderLabel(gender?: string | null): string {
  if (!gender) return 'Chưa rõ';
  const upper = gender.toUpperCase();
  if (upper === 'FEMALE' || upper === 'NU' || upper === 'NỮ') return 'Nữ';
  if (upper === 'MALE' || upper === 'NAM') return 'Nam';
  return gender;
}

export function DiagnosticOrderReviewTable({
  items,
  selectedOrderCode,
  selectedBatchIds,
  onSelectOrder,
  onToggleBatchOrder,
  onToggleAllEligible,
}: DiagnosticOrderReviewTableProps) {
  const eligibleIds = items
    .filter((item) => item.status === 'ORDERED' && item.canConfirm)
    .map((item) => item.orderId);

  const allEligibleSelected =
    eligibleIds.length > 0 && eligibleIds.every((id) => selectedBatchIds.includes(id));

  if (items.length === 0) {
    return (
      <div
        className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-canvas/60 px-6 py-10 text-center"
        data-testid="diagnostic-review-empty-state"
      >
        <AlertCircle className="mb-2 h-6 w-6 text-sub" aria-hidden="true" />
        <p className="text-xs font-semibold text-navy">
          Không tìm thấy phiếu chỉ định cận lâm sàng phù hợp với bộ lọc hiện tại.
        </p>
        <p className="mt-1 text-[11px] text-sub">
          Hãy thử đổi từ khóa tìm kiếm, nhóm kỹ thuật hoặc bộ lọc trạng thái.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left border-collapse" data-testid="diagnostic-review-table">
        <thead>
          <tr className="border-b border-border text-[11px] font-bold uppercase tracking-wider text-sub">
            <th className="py-2.5 pr-2 w-8">
              <input
                type="checkbox"
                aria-label="Chọn tất cả chỉ định đủ điều kiện xác nhận"
                checked={allEligibleSelected}
                disabled={eligibleIds.length === 0}
                onChange={() => onToggleAllEligible(eligibleIds)}
                className="h-4 w-4 rounded border-border text-primary focus:ring-primary disabled:opacity-50"
              />
            </th>
            <th className="py-2.5 pr-3">Số TT / Mã CĐ</th>
            <th className="py-2.5 pr-3">Bệnh nhân</th>
            <th className="py-2.5 pr-3">Dịch vụ & Nhóm CLS</th>
            <th className="py-2.5 pr-3">Phòng / Điểm PV</th>
            <th className="py-2.5 pr-3">Mức ưu tiên</th>
            <th className="py-2.5 pr-3">Trạng thái & Điều kiện</th>
            <th className="py-2.5 text-right">Thao tác</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border text-xs">
          {items.map((item) => {
            const isSelected = selectedOrderCode === item.orderCode;
            const isBatchChecked = selectedBatchIds.includes(item.orderId);
            const isSelectableForBatch = item.status === 'ORDERED' && item.canConfirm;

            const statusKey = (item.status?.toUpperCase() || 'ORDERED') as DiagnosticOrderStatus;
            const statusMeta = STATUS_META[statusKey] ?? {
              label: item.status,
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

            return (
              <tr
                key={item.orderCode}
                data-testid={`review-order-row-${item.orderCode}`}
                onClick={() => onSelectOrder(item)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/90' : 'hover:bg-canvas'
                }`}
              >
                <td
                  className="py-3 pr-2 align-top"
                  onClick={(e) => {
                    e.stopPropagation();
                  }}
                >
                  <input
                    type="checkbox"
                    aria-label={`Chọn phiếu ${item.orderCode}`}
                    checked={isBatchChecked}
                    disabled={!isSelectableForBatch}
                    onChange={() => onToggleBatchOrder(item.orderId)}
                    className="h-4 w-4 rounded border-border text-primary focus:ring-primary disabled:opacity-40"
                  />
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="flex items-center gap-1.5">
                    <span className="font-mono text-xs font-bold text-primary">
                      {item.ticketNo ?? '--'}
                    </span>
                    <span className="text-[10px] text-sub">
                      • {formatOrderTime(item.orderedAt)}
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
                    {item.requiredForConclusion && (
                      <span className="rounded border border-sky-200 bg-sky-50 px-1.5 py-0.5 text-[10px] font-semibold text-sky-700">
                        Bắt buộc kết luận
                      </span>
                    )}
                  </div>
                  <div className="mt-1 text-[10px] text-sub">
                    BS: {item.orderingDoctorName} •{' '}
                    <span className="font-mono">{item.visitCode}</span>
                  </div>
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="font-mono text-xs font-semibold text-navy">
                    {item.roomCode ?? 'Chưa gán phòng'}
                  </div>
                  <div className="text-[11px] text-sub">
                    {item.servicePointName ?? item.roomName ?? 'Chưa chọn điểm phục vụ'}
                  </div>
                </td>

                <td className="py-3 pr-3 align-top">
                  <Badge tone={priorityMeta.tone}>{priorityMeta.label}</Badge>
                </td>

                <td className="py-3 pr-3 align-top">
                  <div className="flex flex-col items-start gap-1">
                    <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                    {item.status === 'ORDERED' && !item.canConfirm && (
                      <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-rose-600">
                        <AlertCircle className="h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>Chưa đủ ĐK xác nhận</span>
                      </span>
                    )}
                    {item.status === 'ORDERED' && item.canConfirm && (
                      <span className="inline-flex items-center gap-1 text-[10px] font-medium text-emerald-700">
                        <CheckCircle2 className="h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>Đủ điều kiện</span>
                      </span>
                    )}
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
                      item.status === 'ORDERED' && item.canConfirm
                        ? 'border-primary bg-primary text-white hover:bg-primary-dark'
                        : 'border-border bg-white text-navy hover:bg-canvas'
                    }`}
                  >
                    {item.status === 'ORDERED' && item.canConfirm ? (
                      <>
                        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Kiểm tra & Xác nhận</span>
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
