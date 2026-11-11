import {
  Activity,
  AlertTriangle,
  Ban,
  CheckCircle2,
  Clock,
  FileText,
  History,
  Megaphone,
  Play,
  RotateCcw,
  ShieldAlert,
  SkipForward,
  Stethoscope,
  UserCheck,
} from 'lucide-react';
import { useState } from 'react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import {
  CANCEL_QUEUE_REASON_PRESETS,
  MISSED_TURN_REASON_PRESETS,
  cancelDiagnosticQueueEntrySchema,
  missedTurnDiagnosticQueueSchema,
  restoreTurnDiagnosticQueueSchema,
  updateDiagnosticQueueStatusSchema,
} from '../schemas/diagnostic-queue.schema';
import type {
  CancelDiagnosticQueueEntryRequest,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticQueueEntryItemDto,
  DiagnosticQueuePresenceStatus,
  DiagnosticQueueTargetStatus,
  DiagnosticQueueTurnActionRequest,
  DiagnosticServicePointOptionDto,
  QueueStatusTransitionHistoryItemDto,
  UpdateDiagnosticQueueStatusRequest,
} from '../types/diagnostic.types';

type ActiveActionTab = 'quick' | 'missed-turn' | 'restore-turn' | 'cancel' | 'status';

interface DiagnosticQueueEntryDetailPanelProps {
  entry: DiagnosticQueueEntryItemDto | null;
  clinicalContext: DiagnosticOrderClinicalSummaryDto | null;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  statusHistory: QueueStatusTransitionHistoryItemDto[];
  hasManagePermission: boolean;
  isSubmitting: boolean;
  onMarkReady: (idOrCode: string, payload?: DiagnosticQueueTurnActionRequest) => void;
  onCallEntry: (idOrCode: string, payload?: DiagnosticQueueTurnActionRequest) => void;
  onStartExecution: (idOrCode: string, payload?: DiagnosticQueueTurnActionRequest) => void;
  onCompleteExecution: (idOrCode: string, payload?: DiagnosticQueueTurnActionRequest) => void;
  onMarkMissedTurn: (idOrCode: string, payload: DiagnosticQueueTurnActionRequest) => void;
  onRestoreTurn: (idOrCode: string, payload?: DiagnosticQueueTurnActionRequest) => void;
  onCancelEntry: (idOrCode: string, payload: CancelDiagnosticQueueEntryRequest) => void;
  onUpdateStatus: (idOrCode: string, payload: UpdateDiagnosticQueueStatusRequest) => void;
}

const TARGET_STATUS_LABELS: Record<string, string> = {
  WAITING: 'Chờ tiếp nhận (WAITING)',
  READY: 'Sẵn sàng gọi (READY)',
  CALLED: 'Đã gọi vào phòng (CALLED)',
  IN_PROGRESS: 'Đang thực hiện kỹ thuật (IN_PROGRESS)',
  COMPLETED: 'Hoàn tất kỹ thuật (COMPLETED)',
  SKIPPED: 'Qua lượt / Vắng mặt (SKIPPED)',
  CANCELLED: 'Đã hủy lượt (CANCELLED)',
};

function formatTimeDisplay(iso?: string | null): string {
  if (!iso) return '--:--';
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

export function DiagnosticQueueEntryDetailPanel({
  entry,
  clinicalContext,
  availableServicePoints,
  statusHistory,
  hasManagePermission,
  isSubmitting,
  onMarkReady,
  onCallEntry,
  onStartExecution,
  onCompleteExecution,
  onMarkMissedTurn,
  onRestoreTurn,
  onCancelEntry,
  onUpdateStatus,
}: DiagnosticQueueEntryDetailPanelProps) {
  const [activeTab, setActiveTab] = useState<ActiveActionTab>('quick');
  const [quickNote, setQuickNote] = useState('');
  const [missedReason, setMissedReason] = useState('');
  const [missedPresence, setMissedPresence] = useState<DiagnosticQueuePresenceStatus>('LEFT');
  const [missedNotes, setMissedNotes] = useState('');
  const [restoreReason, setRestoreReason] = useState(
    'Bệnh nhân đã quay lại phòng cận lâm sàng và xác nhận có mặt.',
  );
  const [restoreNotes, setRestoreNotes] = useState('');
  const [cancelReason, setCancelReason] = useState('');
  const [cancelNotes, setCancelNotes] = useState('');
  const [cancelLinkedOrder, setCancelLinkedOrder] = useState(false);

  const [customTargetStatus, setCustomTargetStatus] =
    useState<DiagnosticQueueTargetStatus>('READY');
  const [customPresence, setCustomPresence] = useState<DiagnosticQueuePresenceStatus>('PRESENT');
  const [customServicePointId, setCustomServicePointId] = useState<string>('');
  const [customReason, setCustomReason] = useState('');
  const [customNotes, setCustomNotes] = useState('');

  const [formValidationError, setFormValidationError] = useState<string | null>(null);

  if (!entry) {
    return (
      <Card data-testid="diagnostic-queue-detail-empty">
        <CardContent className="py-12 text-center">
          <Stethoscope className="mx-auto mb-2 h-8 w-8 text-sub" aria-hidden="true" />
          <p className="text-xs font-semibold text-navy">
            Chọn một lượt xếp hàng cận lâm sàng bên trái để xem chi tiết & điều phối
          </p>
          <p className="mt-1 text-[11px] text-sub">
            Hiển thị thông tin hành chính, cảnh báo dị ứng, bối cảnh lâm sàng, chuyển trạng thái và
            lịch sử thao tác.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleQuickTransition = (type: 'ready' | 'call' | 'start' | 'complete') => {
    setFormValidationError(null);
    const trimmedNote = quickNote.trim();
    if (trimmedNote.length > 500 || trimmedNote.includes('<') || trimmedNote.includes('>')) {
      setFormValidationError('Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).');
      return;
    }

    const payload: DiagnosticQueueTurnActionRequest = {
      presenceStatus: 'PRESENT',
      notes: trimmedNote || null,
      expectedStateVersion: entry.stateVersion,
    };

    if (type === 'ready') onMarkReady(entry.formattedQueueEntryId, payload);
    if (type === 'call') onCallEntry(entry.formattedQueueEntryId, payload);
    if (type === 'start') onStartExecution(entry.formattedQueueEntryId, payload);
    if (type === 'complete') onCompleteExecution(entry.formattedQueueEntryId, payload);
  };

  const handleSubmitMissedTurn = () => {
    setFormValidationError(null);
    const parsed = missedTurnDiagnosticQueueSchema.safeParse({
      reason: missedReason,
      presenceStatus: missedPresence,
      notes: missedNotes || null,
      expectedStateVersion: entry.stateVersion,
    });

    if (!parsed.success) {
      setFormValidationError(
        parsed.error.issues[0]?.message ??
          'Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.',
      );
      return;
    }

    onMarkMissedTurn(entry.formattedQueueEntryId, {
      reason: parsed.data.reason,
      blockedReason: parsed.data.reason,
      presenceStatus: parsed.data.presenceStatus ?? 'LEFT',
      notes: parsed.data.notes ?? null,
      expectedStateVersion: entry.stateVersion,
    });
    setMissedReason('');
    setMissedNotes('');
  };

  const handleSubmitRestoreTurn = () => {
    setFormValidationError(null);
    const parsed = restoreTurnDiagnosticQueueSchema.safeParse({
      reason: restoreReason || null,
      presenceStatus: 'PRESENT',
      notes: restoreNotes || null,
      expectedStateVersion: entry.stateVersion,
    });

    if (!parsed.success) {
      setFormValidationError(
        parsed.error.issues[0]?.message ?? 'Dữ liệu khôi phục lượt chờ không hợp lệ.',
      );
      return;
    }

    onRestoreTurn(entry.formattedQueueEntryId, {
      reason:
        parsed.data.reason ||
        'Bệnh nhân đã quay lại phòng cận lâm sàng và được khôi phục lượt chờ.',
      presenceStatus: 'PRESENT',
      notes: parsed.data.notes ?? null,
      expectedStateVersion: entry.stateVersion,
    });
  };

  const handleSubmitCancelEntry = () => {
    setFormValidationError(null);
    const parsed = cancelDiagnosticQueueEntrySchema.safeParse({
      reason: cancelReason,
      notes: cancelNotes || null,
      cancelLinkedOrder,
      expectedStateVersion: entry.stateVersion,
    });

    if (!parsed.success) {
      setFormValidationError(
        parsed.error.issues[0]?.message ??
          'Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.',
      );
      return;
    }

    onCancelEntry(entry.formattedQueueEntryId, {
      reason: parsed.data.reason,
      notes: parsed.data.notes ?? null,
      cancelLinkedOrder: parsed.data.cancelLinkedOrder,
      expectedStateVersion: entry.stateVersion,
    });
    setCancelReason('');
    setCancelNotes('');
  };

  const handleSubmitCustomStatus = () => {
    setFormValidationError(null);
    const parsedSpId = customServicePointId ? Number(customServicePointId) : null;
    const chosenSp = parsedSpId
      ? availableServicePoints.find((sp) => sp.servicePointId === parsedSpId)
      : undefined;

    const parsed = updateDiagnosticQueueStatusSchema.safeParse({
      targetStatus: customTargetStatus,
      presenceStatus: customPresence,
      servicePointId: parsedSpId,
      roomId: chosenSp?.roomId ?? null,
      reason: customReason || null,
      notes: customNotes || null,
      cancelLinkedOrder: customTargetStatus === 'CANCELLED' ? cancelLinkedOrder : false,
      expectedStateVersion: entry.stateVersion,
    });

    if (!parsed.success) {
      setFormValidationError(
        parsed.error.issues[0]?.message ?? 'Thông tin cập nhật trạng thái hàng đợi không hợp lệ.',
      );
      return;
    }

    onUpdateStatus(entry.formattedQueueEntryId, {
      targetStatus: parsed.data.targetStatus,
      status: parsed.data.targetStatus,
      presenceStatus: parsed.data.presenceStatus,
      servicePointId: parsed.data.servicePointId ?? null,
      roomId: parsed.data.roomId ?? null,
      reason: parsed.data.reason ?? null,
      blockedReason:
        parsed.data.targetStatus === 'SKIPPED' || parsed.data.targetStatus === 'CANCELLED'
          ? (parsed.data.reason ?? null)
          : null,
      notes: parsed.data.notes ?? null,
      cancelLinkedOrder: parsed.data.cancelLinkedOrder ?? false,
      expectedStateVersion: entry.stateVersion,
    });
  };

  return (
    <Card data-testid="diagnostic-queue-detail-panel">
      <CardHeader className="border-b border-border pb-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <div className="flex items-center gap-2">
              <span className="inline-flex h-7 items-center rounded-lg bg-navy px-2.5 font-mono text-sm font-bold text-white">
                {entry.ticketNo}
              </span>
              <span className="font-mono text-xs font-bold text-navy">
                {entry.formattedQueueEntryId}
              </span>
              {entry.orderCode && (
                <Badge tone="sky" className="font-mono">
                  {entry.orderCode}
                </Badge>
              )}
            </div>
            <h2 className="mt-1.5 text-sm font-bold text-ink">{entry.patientName}</h2>
            <p className="font-mono text-[11px] text-sub">
              {entry.patientCode} • {entry.patientGenderLabel}
              {entry.patientAge ? ` (${entry.patientAge} tuổi)` : ''}
              {entry.bloodType ? ` • Nhóm máu ${entry.bloodType}` : ''} • Lượt khám:{' '}
              {entry.visitCode}
            </p>
          </div>

          <div className="text-right">
            <Badge
              tone={
                entry.status === 'IN_PROGRESS'
                  ? 'emerald'
                  : entry.status === 'READY'
                    ? 'sky'
                    : entry.status === 'SKIPPED' || entry.status === 'CANCELLED'
                      ? 'rose'
                      : 'amber'
              }
            >
              {entry.statusLabel}
            </Badge>
            <div className="mt-1 font-mono text-[10px] text-sub">
              Phiên bản: v{entry.stateVersion} • Thứ tự #{entry.servingOrder}
            </div>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4 pt-4">
        {/* Allergy Warning */}
        {entry.allergiesSummary && (
          <div
            className="flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-xs text-rose-800"
            data-testid="patient-allergy-warning"
          >
            <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
            <div>
              <div className="font-bold">Cảnh báo tiền sử dị ứng / lưu ý an toàn</div>
              <div className="mt-0.5">{entry.allergiesSummary}</div>
            </div>
          </div>
        )}

        {/* Blocked / Ineligible Reason */}
        {(entry.blockedReason || entry.ineligibleReason) && (
          <div
            className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-900"
            data-testid="queue-entry-blocked-banner"
          >
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
            <div>
              <span className="font-semibold">Ghi chú trạng thái hàng đợi: </span>
              <span>{entry.blockedReason || entry.ineligibleReason}</span>
            </div>
          </div>
        )}

        {/* Service & Timestamps */}
        <div className="rounded-xl border border-border bg-canvas p-3 text-xs space-y-2">
          <div className="flex items-start justify-between gap-2">
            <div>
              <div className="text-[10px] font-semibold uppercase text-sub">
                Dịch vụ & Phòng thực hiện
              </div>
              <div className="font-bold text-navy">{entry.serviceName}</div>
              <div className="mt-0.5 text-[11px] text-sub">
                <span className="font-mono font-semibold text-ink">{entry.roomCode}</span> •{' '}
                {entry.servicePointName} ({entry.departmentName})
              </div>
            </div>
            <Badge
              tone={
                entry.priority === 'STAT' ? 'rose' : entry.priority === 'URGENT' ? 'amber' : 'slate'
              }
            >
              {entry.priorityLabel}
            </Badge>
          </div>

          {entry.instruction && (
            <div className="rounded-lg border border-border bg-white px-2.5 py-1.5 text-[11px] text-ink">
              <span className="font-semibold text-navy">Hướng dẫn kỹ thuật: </span>
              {entry.instruction}
            </div>
          )}

          <div className="grid grid-cols-2 gap-2 border-t border-border pt-2 text-[11px] sm:grid-cols-4">
            <div>
              <span className="text-sub">Đến quầy:</span>{' '}
              <strong className="font-mono text-navy">{formatTimeDisplay(entry.arrivedAt)}</strong>
            </div>
            <div>
              <span className="text-sub">Sẵn sàng:</span>{' '}
              <strong className="font-mono text-navy">{formatTimeDisplay(entry.readyAt)}</strong>
            </div>
            <div>
              <span className="text-sub">Gọi số:</span>{' '}
              <strong className="font-mono text-navy">{formatTimeDisplay(entry.calledAt)}</strong>
            </div>
            <div>
              <span className="text-sub">Bắt đầu:</span>{' '}
              <strong className="font-mono text-navy">{formatTimeDisplay(entry.startedAt)}</strong>
            </div>
          </div>
        </div>

        {/* Clinical Context */}
        {clinicalContext && (
          <div
            className="rounded-xl border border-border bg-white p-3 text-xs space-y-1.5"
            data-testid="queue-entry-clinical-context"
          >
            <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase text-navy">
              <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Bối cảnh lâm sàng từ Bác sĩ ({entry.orderingDoctorName})</span>
            </div>

            {clinicalContext.primaryDiagnosisCode && (
              <div className="text-[11px]">
                <span className="text-sub">Chẩn đoán ICD-10: </span>
                <span className="font-mono font-bold text-primary">
                  {clinicalContext.primaryDiagnosisCode}
                </span>{' '}
                —{' '}
                <span className="font-semibold text-ink">
                  {clinicalContext.primaryDiagnosisName}
                </span>
              </div>
            )}

            {clinicalContext.reasonForVisit && (
              <div className="text-[11px] text-ink">
                <span className="font-semibold text-sub">Lý do khám: </span>
                {clinicalContext.reasonForVisit}
              </div>
            )}

            {clinicalContext.clinicalFindings && (
              <div className="text-[11px] text-ink">
                <span className="font-semibold text-sub">Khám lâm sàng & Sinh hiệu: </span>
                {clinicalContext.clinicalFindings}
              </div>
            )}
          </div>
        )}

        {/* RBAC Gate */}
        {!hasManagePermission ? (
          <div
            className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900"
            data-testid="queue-readonly-permission-notice"
          >
            Tài khoản hiện tại chỉ có quyền xem hàng đợi cận lâm sàng. Các thao tác gọi bệnh nhân,
            chuyển trạng thái, đánh dấu qua lượt và hủy lượt yêu cầu quyền{' '}
            <span className="font-mono font-bold">QUEUE_MANAGE</span> hoặc{' '}
            <span className="font-mono font-bold">DIAGNOSTIC_RESULT_WRITE</span>.
          </div>
        ) : (
          <div className="space-y-3 rounded-xl border border-border bg-canvas/70 p-3">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5 text-xs font-bold text-navy">
                <Activity className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Điều phối lượt xếp hàng (UC41)</span>
              </div>
              <span className="font-mono text-[10px] text-sub">
                Hiện diện: {entry.presenceStatusLabel}
              </span>
            </div>

            {/* Action Tabs */}
            <div
              className="flex flex-wrap gap-1 border-b border-border pb-2"
              role="tablist"
              aria-label="Chọn nhóm thao tác hàng đợi CLS"
            >
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'quick'}
                onClick={() => {
                  setActiveTab('quick');
                  setFormValidationError(null);
                }}
                data-testid="tab-action-quick"
                className={`rounded-lg px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                  activeTab === 'quick'
                    ? 'bg-primary text-white'
                    : 'bg-white text-sub hover:text-navy'
                }`}
              >
                Chuyển bước nhanh
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'missed-turn'}
                onClick={() => {
                  setActiveTab('missed-turn');
                  setFormValidationError(null);
                }}
                data-testid="tab-action-missed-turn"
                className={`rounded-lg px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                  activeTab === 'missed-turn'
                    ? 'bg-amber-600 text-white'
                    : 'bg-white text-sub hover:text-navy'
                }`}
              >
                Qua lượt (SKIPPED)
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'restore-turn'}
                onClick={() => {
                  setActiveTab('restore-turn');
                  setFormValidationError(null);
                }}
                data-testid="tab-action-restore-turn"
                className={`rounded-lg px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                  activeTab === 'restore-turn'
                    ? 'bg-emerald-600 text-white'
                    : 'bg-white text-sub hover:text-navy'
                }`}
              >
                Khôi phục lượt
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'cancel'}
                onClick={() => {
                  setActiveTab('cancel');
                  setFormValidationError(null);
                }}
                data-testid="tab-action-cancel"
                className={`rounded-lg px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                  activeTab === 'cancel'
                    ? 'bg-rose-600 text-white'
                    : 'bg-white text-sub hover:text-navy'
                }`}
              >
                Hủy lượt
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'status'}
                onClick={() => {
                  setActiveTab('status');
                  setFormValidationError(null);
                }}
                data-testid="tab-action-status"
                className={`rounded-lg px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                  activeTab === 'status'
                    ? 'bg-navy text-white'
                    : 'bg-white text-sub hover:text-navy'
                }`}
              >
                Cập nhật / Đổi phòng
              </button>
            </div>

            {formValidationError && (
              <div
                role="alert"
                data-testid="queue-action-validation-error"
                className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700"
              >
                {formValidationError}
              </div>
            )}

            {/* Tab 1: Quick Workflow Transitions */}
            {activeTab === 'quick' && (
              <div className="space-y-2.5" data-testid="panel-quick-actions">
                <div>
                  <label
                    htmlFor="quick-transition-note"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Ghi chú điều phối (tùy chọn, tối đa 500 ký tự)
                  </label>
                  <input
                    id="quick-transition-note"
                    type="text"
                    value={quickNote}
                    onChange={(e) => setQuickNote(e.target.value)}
                    placeholder="Nhập ghi chú khi gọi số / bắt đầu / hoàn tất kỹ thuật..."
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    disabled={
                      isSubmitting || (entry.status !== 'WAITING' && entry.status !== 'RESERVED')
                    }
                    onClick={() => handleQuickTransition('ready')}
                    data-testid="detail-btn-mark-ready"
                  >
                    <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Đánh dấu Sẵn sàng</span>
                  </Button>

                  <Button
                    type="button"
                    size="sm"
                    variant="primary"
                    disabled={isSubmitting || !entry.canCall}
                    onClick={() => handleQuickTransition('call')}
                    data-testid="detail-btn-call-entry"
                  >
                    <Megaphone className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Gọi bệnh nhân</span>
                  </Button>

                  <Button
                    type="button"
                    size="sm"
                    variant="primary"
                    disabled={isSubmitting || !entry.canStart}
                    onClick={() => handleQuickTransition('start')}
                    data-testid="detail-btn-start-execution"
                  >
                    <Play className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Bắt đầu thực hiện</span>
                  </Button>

                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    disabled={isSubmitting || !entry.canComplete}
                    onClick={() => handleQuickTransition('complete')}
                    data-testid="detail-btn-complete-execution"
                  >
                    <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Hoàn tất kỹ thuật</span>
                  </Button>
                </div>
              </div>
            )}

            {/* Tab 2: Missed Turn (SKIPPED) */}
            {activeTab === 'missed-turn' && (
              <div className="space-y-2.5" data-testid="panel-missed-turn-form">
                <div>
                  <label
                    htmlFor="missed-turn-reason"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do đánh dấu qua lượt / vắng mặt (bắt buộc 5–500 ký tự) *
                  </label>
                  <textarea
                    id="missed-turn-reason"
                    rows={2}
                    value={missedReason}
                    onChange={(e) => setMissedReason(e.target.value)}
                    placeholder="Nhập lý do bệnh nhân qua lượt hoặc chọn mẫu bên dưới..."
                    className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                  />
                  <div className="mt-1 flex flex-wrap gap-1">
                    {MISSED_TURN_REASON_PRESETS.map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        onClick={() => setMissedReason(preset)}
                        className="rounded border border-border bg-white px-2 py-0.5 text-[10px] text-sub hover:border-primary hover:text-navy"
                      >
                        {preset}
                      </button>
                    ))}
                  </div>
                </div>

                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  <div>
                    <label
                      htmlFor="missed-turn-presence"
                      className="block text-[11px] font-semibold text-navy"
                    >
                      Trạng thái hiện diện
                    </label>
                    <select
                      id="missed-turn-presence"
                      value={missedPresence}
                      onChange={(e) =>
                        setMissedPresence(e.target.value as DiagnosticQueuePresenceStatus)
                      }
                      className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2 text-xs text-navy"
                    >
                      <option value="LEFT">Tạm rời khu CLS (LEFT)</option>
                      <option value="NOT_ARRIVED">Chưa đến khu CLS (NOT_ARRIVED)</option>
                      <option value="PRESENT">Có mặt nhưng hoãn kỹ thuật (PRESENT)</option>
                    </select>
                  </div>

                  <div>
                    <label
                      htmlFor="missed-turn-notes"
                      className="block text-[11px] font-semibold text-navy"
                    >
                      Ghi chú thêm (tùy chọn)
                    </label>
                    <input
                      id="missed-turn-notes"
                      type="text"
                      value={missedNotes}
                      onChange={(e) => setMissedNotes(e.target.value)}
                      placeholder="Ghi chú bổ sung..."
                      className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2 text-xs text-ink"
                    />
                  </div>
                </div>

                <Button
                  type="button"
                  size="sm"
                  disabled={isSubmitting || !entry.canSkip}
                  loading={isSubmitting}
                  onClick={handleSubmitMissedTurn}
                  data-testid="submit-missed-turn-btn"
                >
                  <SkipForward className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Xác nhận đánh dấu qua lượt (SKIPPED)</span>
                </Button>
              </div>
            )}

            {/* Tab 3: Restore Skipped Turn (SKIPPED -> READY) */}
            {activeTab === 'restore-turn' && (
              <div className="space-y-2.5" data-testid="panel-restore-turn-form">
                <div>
                  <label
                    htmlFor="restore-turn-reason"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do khôi phục lượt chờ (tối đa 500 ký tự)
                  </label>
                  <textarea
                    id="restore-turn-reason"
                    rows={2}
                    value={restoreReason}
                    onChange={(e) => setRestoreReason(e.target.value)}
                    placeholder="Ghi nhận lý do bệnh nhân quay lại phòng kỹ thuật..."
                    className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                  />
                </div>

                <div>
                  <label
                    htmlFor="restore-turn-notes"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Ghi chú điều phối (tùy chọn)
                  </label>
                  <input
                    id="restore-turn-notes"
                    type="text"
                    value={restoreNotes}
                    onChange={(e) => setRestoreNotes(e.target.value)}
                    placeholder="Ghi chú khi xếp lại vào hàng đợi READY..."
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink"
                  />
                </div>

                <Button
                  type="button"
                  size="sm"
                  disabled={isSubmitting || !entry.canRestore}
                  loading={isSubmitting}
                  onClick={handleSubmitRestoreTurn}
                  data-testid="submit-restore-turn-btn"
                >
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Khôi phục về Sẵn sàng (READY)</span>
                </Button>
              </div>
            )}

            {/* Tab 4: Cancel Queue Entry (-> CANCELLED) */}
            {activeTab === 'cancel' && (
              <div className="space-y-2.5" data-testid="panel-cancel-entry-form">
                <div>
                  <label
                    htmlFor="cancel-queue-reason"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do hủy lượt xếp hàng cận lâm sàng (bắt buộc 5–500 ký tự) *
                  </label>
                  <textarea
                    id="cancel-queue-reason"
                    rows={2}
                    value={cancelReason}
                    onChange={(e) => setCancelReason(e.target.value)}
                    placeholder="Nhập lý do hủy lượt xếp hàng cận lâm sàng..."
                    className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
                  />
                  <div className="mt-1 flex flex-wrap gap-1">
                    {CANCEL_QUEUE_REASON_PRESETS.map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        onClick={() => setCancelReason(preset)}
                        className="rounded border border-border bg-white px-2 py-0.5 text-[10px] text-sub hover:border-rose-300 hover:text-rose-700"
                      >
                        {preset}
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label
                    htmlFor="cancel-queue-notes"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Ghi chú bổ sung (tùy chọn)
                  </label>
                  <input
                    id="cancel-queue-notes"
                    type="text"
                    value={cancelNotes}
                    onChange={(e) => setCancelNotes(e.target.value)}
                    placeholder="Ghi chú bàn giao..."
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink"
                  />
                </div>

                <label className="flex cursor-pointer items-center gap-2 text-xs font-medium text-navy">
                  <input
                    type="checkbox"
                    checked={cancelLinkedOrder}
                    onChange={(e) => setCancelLinkedOrder(e.target.checked)}
                    className="h-3.5 w-3.5 rounded border-border text-rose-600 focus:ring-rose-500"
                  />
                  <span>Đồng thời hủy luôn phiếu chỉ định CLS liên kết ({entry.orderCode})</span>
                </label>

                <Button
                  type="button"
                  size="sm"
                  variant="danger"
                  disabled={isSubmitting || !entry.canCancel}
                  loading={isSubmitting}
                  onClick={handleSubmitCancelEntry}
                  data-testid="submit-cancel-entry-btn"
                >
                  <Ban className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Xác nhận hủy lượt xếp hàng</span>
                </Button>
              </div>
            )}

            {/* Tab 5: Custom Status / Reassign Service Point */}
            {activeTab === 'status' && (
              <div className="space-y-2.5" data-testid="panel-custom-status-form">
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  <div>
                    <label
                      htmlFor="custom-target-status"
                      className="block text-[11px] font-semibold text-navy"
                    >
                      Trạng thái đích
                    </label>
                    <select
                      id="custom-target-status"
                      value={customTargetStatus}
                      onChange={(e) =>
                        setCustomTargetStatus(e.target.value as DiagnosticQueueTargetStatus)
                      }
                      className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2 text-xs text-navy"
                    >
                      {(entry.allowedTargetStatuses.length > 0
                        ? entry.allowedTargetStatuses
                        : ['READY', 'CALLED', 'IN_PROGRESS', 'COMPLETED', 'SKIPPED', 'CANCELLED']
                      ).map((st) => (
                        <option key={st} value={st}>
                          {TARGET_STATUS_LABELS[st] ?? st}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label
                      htmlFor="custom-presence-status"
                      className="block text-[11px] font-semibold text-navy"
                    >
                      Trạng thái hiện diện
                    </label>
                    <select
                      id="custom-presence-status"
                      value={customPresence}
                      onChange={(e) =>
                        setCustomPresence(e.target.value as DiagnosticQueuePresenceStatus)
                      }
                      className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2 text-xs text-navy"
                    >
                      <option value="PRESENT">Có mặt tại khu CLS (PRESENT)</option>
                      <option value="NOT_ARRIVED">Chưa đến khu CLS (NOT_ARRIVED)</option>
                      <option value="LEFT">Tạm rời khu CLS (LEFT)</option>
                    </select>
                  </div>
                </div>

                <div>
                  <label
                    htmlFor="custom-service-point"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Điều chuyển điểm phục vụ / phòng thực hiện (tùy chọn)
                  </label>
                  <select
                    id="custom-service-point"
                    value={customServicePointId}
                    onChange={(e) => setCustomServicePointId(e.target.value)}
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2 text-xs text-navy"
                  >
                    <option value="">
                      Giữ nguyên ({entry.roomCode} • {entry.servicePointName})
                    </option>
                    {availableServicePoints.map((sp) => (
                      <option key={sp.servicePointId} value={String(sp.servicePointId)}>
                        {sp.roomCode} • {sp.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label
                    htmlFor="custom-status-reason"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do cập nhật / điều chuyển
                  </label>
                  <input
                    id="custom-status-reason"
                    type="text"
                    value={customReason}
                    onChange={(e) => setCustomReason(e.target.value)}
                    placeholder="Nhập lý do cập nhật trạng thái hoặc điều chuyển phòng..."
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink"
                  />
                </div>

                <div>
                  <label
                    htmlFor="custom-status-notes"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Ghi chú (tùy chọn)
                  </label>
                  <input
                    id="custom-status-notes"
                    type="text"
                    value={customNotes}
                    onChange={(e) => setCustomNotes(e.target.value)}
                    placeholder="Ghi chú nội bộ..."
                    className="mt-1 h-8 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink"
                  />
                </div>

                <Button
                  type="button"
                  size="sm"
                  disabled={isSubmitting || entry.allowedTargetStatuses.length === 0}
                  loading={isSubmitting}
                  onClick={handleSubmitCustomStatus}
                  data-testid="submit-custom-status-btn"
                >
                  <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Cập nhật trạng thái hàng đợi</span>
                </Button>
              </div>
            )}
          </div>
        )}

        {/* Status Transition History Timeline */}
        <div
          className="rounded-xl border border-border bg-white p-3 text-xs"
          data-testid="queue-status-history-section"
        >
          <div className="mb-2 flex items-center justify-between">
            <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase text-navy">
              <History className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Lịch sử chuyển trạng thái (Audit Log)</span>
            </div>
            <Badge tone="slate">{statusHistory.length} bản ghi</Badge>
          </div>

          {statusHistory.length === 0 ? (
            <p className="text-[11px] italic text-sub">
              Chưa có lịch sử chuyển trạng thái cho lượt xếp hàng này.
            </p>
          ) : (
            <ul className="divide-y divide-border" data-testid="queue-status-history-list">
              {statusHistory.map((item) => (
                <li key={item.auditLogId} className="py-2 first:pt-0 last:pb-0">
                  <div className="flex items-center justify-between gap-2">
                    <div className="font-mono text-[11px] font-bold text-navy">
                      {item.previousStatus || 'INIT'} → {item.targetStatus} (v
                      {item.updatedStateVersion})
                    </div>
                    <span className="font-mono text-[10px] text-sub">
                      {formatTimeDisplay(item.updatedAtUtc)}
                    </span>
                  </div>
                  {(item.reason || item.blockedReason) && (
                    <div className="mt-0.5 text-[11px] text-ink">
                      {item.reason || item.blockedReason}
                    </div>
                  )}
                  <div className="mt-0.5 text-[10px] text-sub">
                    Thực hiện bởi: {item.updatedByName ?? 'KTV Cận lâm sàng'}
                    {item.updatedByStaffCode ? ` (${item.updatedByStaffCode})` : ''}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
