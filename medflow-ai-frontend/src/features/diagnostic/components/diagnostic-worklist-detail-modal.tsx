import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  FileText,
  GitBranch,
  Play,
  Stethoscope,
  UserCheck,
} from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import type {
  DiagnosticWorklistActionDto,
  DiagnosticWorklistActionId,
  DiagnosticWorklistItemDto,
} from '../types/diagnostic-worklist.types';

interface DiagnosticWorklistDetailModalProps {
  open: boolean;
  item: DiagnosticWorklistItemDto | null;
  isLoading?: boolean;
  error?: unknown;
  canConfirmOrders: boolean;
  canManageQueue: boolean;
  canWriteResults: boolean;
  onClose: () => void;
  onRetry?: () => void;
  onTriggerAction: (item: DiagnosticWorklistItemDto, actionId: DiagnosticWorklistActionId) => void;
}

function formatCurrencyVnd(amount: number): string {
  return `${amount.toLocaleString('vi-VN')} đ`;
}

function isActionPermittedForStaff(
  action: DiagnosticWorklistActionDto,
  permissions: {
    canConfirmOrders: boolean;
    canManageQueue: boolean;
    canWriteResults: boolean;
  },
): boolean {
  switch (action.id) {
    case 'confirm-order':
      return permissions.canConfirmOrders;
    case 'receive-patient':
      return permissions.canManageQueue;
    case 'update-execution':
      return permissions.canManageQueue || permissions.canWriteResults;
    case 'enter-result':
      return permissions.canWriteResults;
    case 'view-result':
      return true;
    default:
      return false;
  }
}

function getActionIcon(actionId: string) {
  switch (actionId) {
    case 'confirm-order':
      return <CheckCircle2 className="h-4 w-4" aria-hidden="true" />;
    case 'receive-patient':
      return <UserCheck className="h-4 w-4" aria-hidden="true" />;
    case 'update-execution':
      return <Play className="h-4 w-4" aria-hidden="true" />;
    default:
      return <FileText className="h-4 w-4" aria-hidden="true" />;
  }
}

export function DiagnosticWorklistDetailModal({
  open,
  item,
  isLoading = false,
  error = null,
  canConfirmOrders,
  canManageQueue,
  canWriteResults,
  onClose,
  onRetry,
  onTriggerAction,
}: DiagnosticWorklistDetailModalProps) {
  const permittedActions = (item?.allowedActions ?? []).filter((action) =>
    isActionPermittedForStaff(action, {
      canConfirmOrders,
      canManageQueue,
      canWriteResults,
    }),
  );

  const disabledReasons = permittedActions
    .filter((action) => !action.enabled && action.disabledReason)
    .map((action) => ({
      id: action.id,
      label: action.label,
      reason: action.disabledReason as string,
    }));

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <div className="max-h-[85vh] overflow-y-auto p-6 space-y-5">
        <div className="border-b border-border pb-4 pr-8">
          <div className="text-xs font-mono uppercase tracking-wider text-primary font-bold">
            Phiếu chỉ định cận lâm sàng • UC39
          </div>
          <h2 className="mt-0.5 text-base font-bold text-navy">
            {item
              ? `Chi tiết chỉ định ${item.orderCode} (${item.ticketNo})`
              : 'Chi tiết phiếu chỉ định cận lâm sàng'}
          </h2>
        </div>

        {isLoading && (
          <div
            role="status"
            className="rounded-xl border border-border bg-canvas px-4 py-8 text-center text-xs text-sub"
          >
            Đang tải thông tin chi tiết phiếu chỉ định cận lâm sàng...
          </div>
        )}

        {Boolean(error) && !isLoading && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                {getErrorMessage(error, 'Không thể tải chi tiết phiếu chỉ định cận lâm sàng.')}
              </span>
              {onRetry && (
                <button
                  type="button"
                  onClick={onRetry}
                  className="rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 hover:bg-rose-50"
                >
                  Thử lại
                </button>
              )}
            </div>
          </Alert>
        )}

        {item && !isLoading && (
          <div className="space-y-4 text-xs">
            {/* Header badges & pricing */}
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-canvas p-3.5">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-sm font-bold text-navy">
                  Số thứ tự: {item.ticketNo}
                </span>
                <Badge
                  tone={
                    item.priority === 'STAT'
                      ? 'rose'
                      : item.priority === 'URGENT'
                        ? 'amber'
                        : 'slate'
                  }
                >
                  {item.priorityLabel}
                </Badge>
                <Badge tone="sky">{item.statusLabel}</Badge>
                <Badge tone="emerald">{item.categoryLabel}</Badge>
                {item.requiredForConclusion && <Badge tone="amber">Bắt buộc cho kết luận</Badge>}
              </div>
              <div className="font-mono text-xs font-bold text-navy">
                Đơn giá: {formatCurrencyVnd(item.unitPrice)}
              </div>
            </div>

            {/* Allergy Warning */}
            {item.allergiesSummary && (
              <div
                role="note"
                className="flex items-center gap-2.5 rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-2.5 text-xs font-semibold text-rose-700"
              >
                <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span>Cảnh báo dị ứng: {item.allergiesSummary}</span>
              </div>
            )}

            {/* Additional Order Lineage */}
            {item.isAdditionalOrder && (
              <div className="rounded-xl border border-amber-200 bg-amber-50/80 p-3.5 space-y-1">
                <div className="flex items-center gap-1.5 font-bold text-amber-900">
                  <GitBranch className="h-4 w-4" aria-hidden="true" />
                  <span>
                    Chỉ định bổ sung lần #{item.orderSequenceNo}
                    {item.followUpToOrderCode
                      ? ` • Tiếp nối từ phiếu ${item.followUpToOrderCode}`
                      : ''}
                  </span>
                </div>
                {item.additionalOrderReason && (
                  <p className="text-amber-800">
                    Lý do chỉ định bổ sung: <strong>{item.additionalOrderReason}</strong>
                  </p>
                )}
              </div>
            )}

            {/* Patient & Visit Grid */}
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="rounded-xl border border-border bg-white p-3.5 space-y-2">
                <div className="text-[11px] font-bold uppercase tracking-wider text-sub">
                  Thông tin bệnh nhân & lượt khám
                </div>
                <div className="space-y-1">
                  <div className="text-sm font-bold text-navy">{item.patientName}</div>
                  <div className="text-sub">
                    Mã BN: <strong className="font-mono text-ink">{item.patientCode}</strong> •{' '}
                    {item.patientGenderLabel}
                    {typeof item.patientAge === 'number' ? `, ${item.patientAge} tuổi` : ''}
                    {item.bloodType ? ` • Nhóm máu ${item.bloodType}` : ''}
                  </div>
                  <div className="text-sub">
                    Mã lượt khám: <strong className="font-mono text-navy">{item.visitCode}</strong>{' '}
                    (<span className="font-mono">{item.visitStatus}</span>)
                  </div>
                </div>
              </div>

              <div className="rounded-xl border border-border bg-white p-3.5 space-y-2">
                <div className="text-[11px] font-bold uppercase tracking-wider text-sub">
                  Dịch vụ & Điểm thực hiện CLS
                </div>
                <div className="space-y-1">
                  <div className="text-sm font-bold text-navy">{item.serviceName}</div>
                  <div className="text-sub">
                    Mã DV: <strong className="font-mono text-ink">{item.serviceCode}</strong> •
                    Chuyên khoa: {item.specialtyName}
                  </div>
                  <div className="text-sub">
                    Phòng thực hiện: <strong className="font-mono text-primary">{item.room}</strong>
                    {item.servicePointName ? ` (${item.servicePointName})` : ''}
                  </div>
                </div>
              </div>
            </div>

            {/* Clinical Context */}
            <div className="rounded-xl border border-border bg-white p-3.5 space-y-2">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-sub">
                  <Stethoscope className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                  <span>Thông tin lâm sàng & hướng dẫn chuẩn bị</span>
                </div>
                <span className="text-[11px] text-sub">
                  BS chỉ định: <strong className="text-navy">{item.orderingDoctor}</strong> (
                  <span className="font-mono">{item.orderedAt}</span>)
                </span>
              </div>

              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 pt-1">
                <div>
                  <span className="text-sub">Lý do khám: </span>
                  <span className="font-medium text-ink">
                    {item.reasonForVisit || 'Chưa ghi nhận'}
                  </span>
                </div>
                <div>
                  <span className="text-sub">Triệu chứng chính: </span>
                  <span className="font-medium text-ink">
                    {item.presentingComplaint || 'Chưa ghi nhận'}
                  </span>
                </div>
                <div className="sm:col-span-2">
                  <span className="text-sub">Khám lâm sàng: </span>
                  <span className="font-medium text-ink">
                    {item.clinicalFindings || 'Chưa ghi nhận'}
                  </span>
                </div>
                {item.instruction && (
                  <div className="sm:col-span-2 rounded-lg bg-sky-50 px-3 py-2 text-sky-900 border border-sky-100">
                    <strong>Hướng dẫn bệnh nhân / kỹ thuật:</strong> {item.instruction}
                  </div>
                )}
              </div>
            </div>

            {/* Execution Timeline & Result */}
            <div className="rounded-xl border border-border bg-white p-3.5 space-y-2">
              <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-sub">
                <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                <span>Tiến độ thực hiện & Kết quả cận lâm sàng</span>
              </div>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div>
                  <span className="text-sub">Trạng thái hàng đợi: </span>
                  <strong className="font-mono text-navy">
                    {item.queueStatus ?? 'CHƯA XẾP HÀNG'}
                  </strong>
                </div>
                <div>
                  <span className="text-sub">Người xác nhận: </span>
                  <strong className="text-ink">
                    {item.confirmedByStaffName ?? 'Chưa xác nhận'}
                  </strong>
                </div>
                <div>
                  <span className="text-sub">Trạng thái kết quả: </span>
                  <strong className="font-mono text-primary">{item.resultStatus}</strong>
                </div>
              </div>

              {(item.resultText || item.conclusion) && (
                <div className="mt-2 rounded-lg border border-emerald-200 bg-emerald-50/60 p-3 space-y-1">
                  {item.resultText && (
                    <div>
                      <span className="font-semibold text-emerald-950">Mô tả kết quả: </span>
                      <span className="text-emerald-900">{item.resultText}</span>
                    </div>
                  )}
                  {item.conclusion && (
                    <div>
                      <span className="font-semibold text-emerald-950">Kết luận CLS: </span>
                      <strong className="text-emerald-900">{item.conclusion}</strong>
                    </div>
                  )}
                  {(item.performedByStaffName || item.verifiedByStaffName) && (
                    <div className="text-[11px] text-emerald-800 pt-1">
                      Thực hiện: {item.performedByStaffName ?? '—'} • Duyệt KQ:{' '}
                      {item.verifiedByStaffName ?? 'Chờ duyệt'}
                    </div>
                  )}
                </div>
              )}
            </div>

            {disabledReasons.length > 0 && (
              <div className="rounded-xl border border-slate-200 bg-canvas px-3.5 py-2.5 text-[11px] text-sub space-y-1">
                {disabledReasons.map((entry) => (
                  <div key={entry.id}>
                    • <strong>{entry.label}:</strong> {entry.reason}
                  </div>
                ))}
              </div>
            )}

            {/* Modal Footer Actions */}
            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-border pt-4">
              <Button variant="secondary" size="sm" onClick={onClose}>
                Đóng cửa sổ
              </Button>

              <div className="flex flex-wrap items-center gap-2">
                {permittedActions
                  .filter((action) => action.enabled)
                  .map((action) => (
                    <Button
                      key={action.id}
                      variant={action.id === 'confirm-order' ? 'primary' : 'secondary'}
                      size="sm"
                      onClick={() => onTriggerAction(item, action.id as DiagnosticWorklistActionId)}
                    >
                      {getActionIcon(action.id)}
                      <span>
                        {action.label} ({action.ucCode})
                      </span>
                    </Button>
                  ))}
              </div>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
