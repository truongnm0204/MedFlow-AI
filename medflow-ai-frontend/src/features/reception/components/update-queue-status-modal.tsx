import { useState, type FormEvent } from 'react';
import axios from 'axios';
import {
  Activity,
  AlertCircle,
  CheckCircle2,
  History,
  ListChecks,
  Stethoscope,
  UserCheck,
} from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { getAllowedNextQueueStatuses } from '../data/examination-queue-mock';
import { useQueueEntryStatusDetail, useUpdateQueueStatus } from '../hooks/use-queue-status-update';
import {
  updateQueueStatusSchema,
  type UpdateQueueStatusFormValues,
} from '../schemas/update-queue-status.schema';
import type {
  ExaminationQueueEntryDto,
  ExaminationQueueStatus,
  QueuePresenceStatus,
} from '../types/examination-queue.types';
import type {
  UpdateQueueStatusRequestPayload,
  UpdateQueueStatusResponseDto,
  UpdateQueueStatusTarget,
} from '../types/queue-status-update.types';
import { EXAMINATION_QUEUE_STATUS_META } from './examination-queue-service-point-card';

export interface UpdateQueueStatusModalResult {
  entryId: string;
  visitCode: string;
  ticketNo: string;
  patientName: string;
  previousStatus: ExaminationQueueStatus;
  currentStatus: ExaminationQueueStatus;
  presenceStatus: QueuePresenceStatus;
  blockedReason: string | null;
  reason: string | null;
  response?: UpdateQueueStatusResponseDto;
}

export interface UpdateQueueStatusModalProps {
  open: boolean;
  target?: UpdateQueueStatusTarget | null;
  entry?: ExaminationQueueEntryDto | null;
  onClose: () => void;
  onUpdated: (result: UpdateQueueStatusModalResult) => void;
  onSubmitOverride?: (
    idOrCode: string,
    payload: UpdateQueueStatusRequestPayload,
  ) => Promise<UpdateQueueStatusResponseDto>;
}

const QUEUE_STATUS_META: Record<
  ExaminationQueueStatus,
  {
    label: string;
    description: string;
    tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose';
  }
> = {
  WAITING: {
    label: 'Đang chờ (WAITING)',
    description: 'Đưa bệnh nhân vào danh sách chờ gọi số tại phòng khám',
    tone: 'amber',
  },
  READY: {
    label: 'Sẵn sàng (READY)',
    description: 'Xác nhận bệnh nhân đã sẵn sàng trước cửa buồng khám',
    tone: 'sky',
  },
  CALLED: {
    label: 'Đã gọi số (CALLED)',
    description: 'Phát tín hiệu gọi bệnh nhân vào buồng khám lâm sàng',
    tone: 'sky',
  },
  IN_PROGRESS: {
    label: 'Đang khám (IN_PROGRESS)',
    description: 'Bác sĩ bắt đầu phiên khám lâm sàng cho bệnh nhân',
    tone: 'emerald',
  },
  COMPLETED: {
    label: 'Hoàn tất (COMPLETED)',
    description: 'Kết thúc lượt khám tại điểm dịch vụ hiện tại',
    tone: 'slate',
  },
  SKIPPED: {
    label: 'Qua lượt (SKIPPED)',
    description: 'Tạm bỏ qua lượt do bệnh nhân vắng mặt khi gọi số (cần nhập lý do)',
    tone: 'rose',
  },
};

const PRESENCE_OPTIONS: ReadonlyArray<{
  value: QueuePresenceStatus;
  label: string;
  tone: 'emerald' | 'amber' | 'rose';
}> = [
  { value: 'PRESENT', label: 'Có mặt', tone: 'emerald' },
  { value: 'NOT_ARRIVED', label: 'Chưa đến', tone: 'amber' },
  { value: 'LEFT', label: 'Vắng mặt', tone: 'rose' },
];

const SKIPPED_REASON_SUGGESTIONS: readonly string[] = [
  'Gọi 3 lần chưa có mặt tại cửa phòng khám',
  'Bệnh nhân xin hoãn lượt tạm thời',
];

type FieldErrors = Partial<Record<keyof UpdateQueueStatusFormValues, string>>;

function shouldFallbackToLocalUpdate(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function toTargetFromEntry(entry: ExaminationQueueEntryDto): UpdateQueueStatusTarget {
  return {
    entryId: entry.id,
    queueNumber: entry.queueNumber,
    ticketNo: entry.ticketNo,
    visitCode: entry.visitCode,
    patientName: entry.patientName,
    patientCode: entry.patientCode,
    roomCode: entry.roomCode,
    roomName: entry.roomName,
    departmentName: entry.departmentName,
    doctorName: entry.doctorName,
    currentStatus: entry.status,
    presenceStatus: entry.presenceStatus,
    priority: entry.priority,
    blockedReason: entry.blockedReason,
  };
}

export function UpdateQueueStatusModal({
  open,
  target,
  entry,
  onClose,
  onUpdated,
  onSubmitOverride,
}: UpdateQueueStatusModalProps) {
  const resolvedTarget = target ?? (entry ? toTargetFromEntry(entry) : null);
  const allowedTargets = resolvedTarget
    ? getAllowedNextQueueStatuses(resolvedTarget.currentStatus)
    : [];
  const defaultTargetStatus: ExaminationQueueStatus =
    resolvedTarget?.initialTargetStatus &&
    allowedTargets.includes(resolvedTarget.initialTargetStatus)
      ? resolvedTarget.initialTargetStatus
      : (allowedTargets[0] ?? resolvedTarget?.currentStatus ?? 'WAITING');

  const [values, setValues] = useState<UpdateQueueStatusFormValues>(() => ({
    queueEntryIdOrVisitCode: resolvedTarget?.entryId || resolvedTarget?.visitCode || '',
    currentStatus: resolvedTarget?.currentStatus,
    targetStatus: defaultTargetStatus,
    presenceStatus:
      defaultTargetStatus === 'SKIPPED' ? 'LEFT' : (resolvedTarget?.presenceStatus ?? 'PRESENT'),
    reason: resolvedTarget?.blockedReason ?? '',
    notes: '',
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusDetailQuery = useQueueEntryStatusDetail(
    values.queueEntryIdOrVisitCode,
    open && Boolean(resolvedTarget),
  );
  const updateMutation = useUpdateQueueStatus();

  if (!open || !resolvedTarget) {
    return null;
  }

  const activeTarget = resolvedTarget;
  const statusHistory = statusDetailQuery.data?.statusHistory ?? [];
  const currentStateVersion =
    statusDetailQuery.data?.stateVersion ?? activeTarget.stateVersion ?? 1;
  const currentStatusMeta = QUEUE_STATUS_META[activeTarget.currentStatus];

  function handleSelectTargetStatus(nextStatus: ExaminationQueueStatus) {
    const suggestedPresence: QueuePresenceStatus =
      nextStatus === 'SKIPPED'
        ? 'LEFT'
        : nextStatus === 'IN_PROGRESS' || nextStatus === 'READY' || nextStatus === 'CALLED'
          ? 'PRESENT'
          : values.presenceStatus;

    setValues((prev) => ({
      ...prev,
      targetStatus: nextStatus,
      presenceStatus: suggestedPresence,
    }));
    setErrors((prev) => ({
      ...prev,
      targetStatus: undefined,
      presenceStatus: undefined,
      reason: undefined,
    }));
    setApiError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    if (allowedTargets.length === 0) {
      setApiError('Lượt xếp hàng đã hoàn tất, không thể chuyển trạng thái.');
      return;
    }

    const parsed = updateQueueStatusSchema.safeParse({
      ...values,
      currentStatus: activeTarget.currentStatus,
    });
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof UpdateQueueStatusFormValues | undefined;
        if (field && !nextErrors[field]) {
          nextErrors[field] = issue.message;
        }
      }
      setErrors(nextErrors);
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    const payload: UpdateQueueStatusRequestPayload = {
      targetStatus: parsed.data.targetStatus,
      presenceStatus: parsed.data.presenceStatus,
      reason: parsed.data.reason.length > 0 ? parsed.data.reason : null,
      blockedReason:
        parsed.data.targetStatus === 'SKIPPED' && parsed.data.reason.length > 0
          ? parsed.data.reason
          : null,
      notes: parsed.data.notes && parsed.data.notes.length > 0 ? parsed.data.notes : null,
      expectedStateVersion: currentStateVersion,
    };
    try {
      const response = onSubmitOverride
        ? await onSubmitOverride(parsed.data.queueEntryIdOrVisitCode, payload)
        : await updateMutation.mutateAsync({
            idOrCode: parsed.data.queueEntryIdOrVisitCode,
            payload,
          });

      onUpdated({
        entryId: activeTarget.entryId,
        visitCode: response.visitCode ?? activeTarget.visitCode,
        ticketNo: response.ticketNo || activeTarget.ticketNo,
        patientName: response.patientName ?? activeTarget.patientName,
        previousStatus: response.previousStatus ?? activeTarget.currentStatus,
        currentStatus: response.currentStatus,
        presenceStatus: response.presenceStatus ?? parsed.data.presenceStatus,
        blockedReason: response.blockedReason,
        reason: response.reason,
        response,
      });
    } catch (error) {
      if (!onSubmitOverride && shouldFallbackToLocalUpdate(error)) {
        onUpdated({
          entryId: activeTarget.entryId,
          visitCode: activeTarget.visitCode,
          ticketNo: activeTarget.ticketNo,
          patientName: activeTarget.patientName,
          previousStatus: activeTarget.currentStatus,
          currentStatus: parsed.data.targetStatus,
          presenceStatus: parsed.data.presenceStatus,
          blockedReason: parsed.data.targetStatus === 'SKIPPED' ? parsed.data.reason : null,
          reason: parsed.data.reason.length > 0 ? parsed.data.reason : null,
        });
        return;
      }

      setApiError(getErrorMessage(error, 'Không thể cập nhật trạng thái hàng đợi khám.'));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="update-queue-status-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2
              id="update-queue-status-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              <ListChecks aria-hidden="true" className="h-5 w-5 text-primary" />
              <span>Cập nhật trạng thái hàng đợi khám (UC28)</span>
            </h2>
            <p className="mt-0.5 text-xs text-sub">
              Chuyển trạng thái lượt xếp hàng theo quy tắc máy trạng thái, đồng bộ trạng thái lượt
              khám và lưu nhật ký kiểm toán.
            </p>
          </div>
          <Badge tone={currentStatusMeta.tone}>{currentStatusMeta.label}</Badge>
        </div>

        {apiError && (
          <div
            role="alert"
            className="mt-4 flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            <AlertCircle aria-hidden="true" className="h-4 w-4 shrink-0" />
            <span>{apiError}</span>
          </div>
        )}

        <div className="mt-4 grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Số phiếu / Bệnh nhân
            </div>
            <div className="mt-0.5 flex items-center gap-2">
              <span className="font-mono text-sm font-extrabold text-primary">
                {activeTarget.ticketNo}
              </span>
              <span className="text-sm font-bold text-ink">{activeTarget.patientName}</span>
            </div>
            <div className="font-mono text-xs text-sub">
              {activeTarget.patientCode} • {activeTarget.visitCode}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Điểm dịch vụ / Bác sĩ
            </div>
            <div className="mt-0.5 flex items-center gap-1.5 text-sm font-semibold text-ink">
              <Stethoscope aria-hidden="true" className="h-4 w-4 text-primary" />
              <span>{activeTarget.doctorName}</span>
            </div>
            <div className="text-xs text-sub">
              {activeTarget.roomCode} • {activeTarget.departmentName}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Phiên bản trạng thái
            </div>
            <div className="mt-0.5 flex items-center gap-1.5 font-mono text-sm font-bold text-navy">
              <Activity aria-hidden="true" className="h-4 w-4 text-emerald-600" />
              <span>v{currentStateVersion}</span>
            </div>
            <div className="text-[11px] text-sub">Giữ nguyên STT #{activeTarget.queueNumber}</div>
          </div>
        </div>

        <div className="mt-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-navy">
              Chọn trạng thái chuyển tiếp hợp lệ ({allowedTargets.length})
            </span>
            <span className="text-[11px] text-sub">
              Chỉ hiển thị các bước chuyển tiếp được phép từ {activeTarget.currentStatus}
            </span>
          </div>

          {allowedTargets.length === 0 ? (
            <div className="mt-2.5 rounded-xl border border-border bg-canvas p-4 text-xs text-sub">
              Lượt xếp hàng này đã ở trạng thái kết thúc ({activeTarget.currentStatus}), không thể
              chuyển tiếp thêm.
            </div>
          ) : (
            <div
              role="radiogroup"
              aria-label="Chọn trạng thái hàng đợi mới"
              className="mt-2.5 grid grid-cols-1 gap-2.5 sm:grid-cols-2"
            >
              {allowedTargets.map((statusOption) => {
                const meta = QUEUE_STATUS_META[statusOption];
                const shortLabel = EXAMINATION_QUEUE_STATUS_META[statusOption].label;
                const isSelected = values.targetStatus === statusOption;

                return (
                  <button
                    key={statusOption}
                    type="button"
                    role="radio"
                    aria-checked={isSelected}
                    onClick={() => handleSelectTargetStatus(statusOption)}
                    className={`flex flex-col justify-between rounded-xl border p-3.5 text-left transition-colors ${
                      isSelected
                        ? 'border-primary bg-sky-50/70 ring-2 ring-primary/30'
                        : 'border-border bg-white hover:border-primary/60'
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-bold text-navy">{meta.label}</span>
                      <Badge tone={meta.tone}>{shortLabel}</Badge>
                    </div>
                    <p className="mt-1.5 text-[11px] text-sub leading-relaxed">
                      {meta.description}
                    </p>
                    {isSelected && (
                      <div className="mt-2 inline-flex items-center gap-1 text-xs font-semibold text-primary">
                        <CheckCircle2 aria-hidden="true" className="h-3.5 w-3.5" />
                        <span>Trạng thái đích đã chọn</span>
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          )}

          {errors.targetStatus && (
            <p role="alert" className="mt-2 text-xs font-semibold text-rose-600">
              {errors.targetStatus}
            </p>
          )}
        </div>

        <div className="mt-5">
          <label
            htmlFor="update-queue-presence-status"
            className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-navy"
          >
            <UserCheck aria-hidden="true" className="h-3.5 w-3.5 text-primary" />
            <span>Trạng thái hiện diện của bệnh nhân</span>
          </label>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            {PRESENCE_OPTIONS.map((option) => {
              const isSelected = values.presenceStatus === option.value;
              return (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => {
                    setValues((prev) => ({ ...prev, presenceStatus: option.value }));
                    setErrors((prev) => ({ ...prev, presenceStatus: undefined }));
                    setApiError(null);
                  }}
                  className={`rounded-xl border px-3.5 py-2 text-xs font-semibold transition-colors ${
                    isSelected
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-white text-ink hover:bg-canvas'
                  }`}
                >
                  {option.label} ({option.value})
                </button>
              );
            })}
            <select
              id="update-queue-presence-status"
              aria-label="Trạng thái hiện diện"
              value={values.presenceStatus}
              onChange={(e) => {
                setValues((prev) => ({
                  ...prev,
                  presenceStatus: e.target.value as QueuePresenceStatus,
                }));
                setErrors((prev) => ({ ...prev, presenceStatus: undefined }));
                setApiError(null);
              }}
              className="sr-only"
            >
              {PRESENCE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
          {errors.presenceStatus && (
            <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
              {errors.presenceStatus}
            </p>
          )}
        </div>

        {values.targetStatus === 'SKIPPED' && (
          <div className="mt-4 flex flex-wrap items-center gap-1.5">
            {SKIPPED_REASON_SUGGESTIONS.map((suggestion) => (
              <button
                key={suggestion}
                type="button"
                onClick={() => {
                  setValues((prev) => ({ ...prev, reason: suggestion }));
                  setErrors((prev) => ({ ...prev, reason: undefined }));
                  setApiError(null);
                }}
                className="rounded-lg border border-border bg-canvas px-2.5 py-1 text-[11px] font-medium text-navy hover:border-primary/50 hover:bg-primary/5 transition-colors"
              >
                {suggestion}
              </button>
            ))}
          </div>
        )}

        <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            id="queue-status-reason"
            label={
              values.targetStatus === 'SKIPPED'
                ? 'Lý do cập nhật / Lý do hoặc ghi chú cập nhật trạng thái (bắt buộc)'
                : 'Lý do cập nhật / Lý do hoặc ghi chú cập nhật trạng thái (tùy chọn)'
            }
            placeholder="Ví dụ: Gọi 3 lần bệnh nhân chưa có mặt tại cửa phòng khám"
            value={values.reason}
            error={errors.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
              setApiError(null);
            }}
          />

          <Input
            id="queue-status-notes"
            label="Ghi chú điều phối (tùy chọn)"
            placeholder="Ví dụ: Đã thông báo qua loa khu vực chờ tầng 1"
            value={values.notes ?? ''}
            error={errors.notes}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, notes: e.target.value }));
              setErrors((prev) => ({ ...prev, notes: undefined }));
            }}
          />
        </div>

        {statusHistory.length > 0 && (
          <div className="mt-5 rounded-xl border border-border bg-canvas p-3.5">
            <div className="flex items-center gap-1.5 text-xs font-bold text-navy">
              <History aria-hidden="true" className="h-4 w-4 text-primary" />
              <span>Lịch sử chuyển trạng thái ({statusHistory.length})</span>
            </div>
            <ul className="mt-2 space-y-1.5 text-xs text-sub">
              {statusHistory.slice(0, 5).map((item) => (
                <li
                  key={item.auditLogId}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-white px-3 py-2"
                >
                  <div>
                    <span className="font-mono font-bold text-navy">
                      {item.previousStatus} → {item.targetStatus}
                    </span>
                    {item.reason && <span className="ml-2 text-ink">({item.reason})</span>}
                  </div>
                  <span className="font-mono text-[11px] text-sub">
                    v{item.updatedStateVersion} • {item.updatedByName ?? 'Nhân viên'}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" aria-label="Hủy bỏ" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button type="submit" disabled={allowedTargets.length === 0} loading={isSubmitting}>
            Xác nhận cập nhật trạng thái
          </Button>
        </div>
      </form>
    </Modal>
  );
}
