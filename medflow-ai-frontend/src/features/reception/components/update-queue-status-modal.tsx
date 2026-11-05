import { useState, type FormEvent } from 'react';
import { ArrowRightLeft, CheckCircle2, DoorOpen, Stethoscope, UserCheck } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { getAllowedNextQueueStatuses } from '../data/examination-queue-mock';
import { useUpdateExaminationQueueStatus } from '../hooks/use-examination-queue';
import {
  updateQueueStatusSchema,
  type UpdateQueueStatusFormValues,
} from '../schemas/update-queue-status.schema';
import type {
  ExaminationQueueEntryDto,
  ExaminationQueueResponseDto,
  ExaminationQueueStatus,
  QueuePresenceStatus,
  UpdateExaminationQueueStatusPayload,
  UpdateExaminationQueueStatusResponseDto,
} from '../types/examination-queue.types';
import {
  EXAMINATION_QUEUE_STATUS_META,
  PRESENCE_STATUS_META,
} from './examination-queue-service-point-card';

export interface UpdateQueueStatusModalProps {
  open: boolean;
  entry: ExaminationQueueEntryDto | null;
  currentQueueData?: ExaminationQueueResponseDto;
  onClose: () => void;
  onUpdated: (result: UpdateExaminationQueueStatusResponseDto) => void;
}

type FieldErrors = Partial<Record<keyof UpdateQueueStatusFormValues, string>>;

const STATUS_TRANSITION_DESCRIPTIONS: Readonly<Record<ExaminationQueueStatus, string>> = {
  READY: 'Bệnh nhân đã có mặt tại cửa phòng và đủ điều kiện chờ bác sĩ gọi.',
  CALLED: 'Phát tín hiệu gọi bệnh nhân vào buồng khám lâm sàng.',
  IN_PROGRESS: 'Bệnh nhân đã vào phòng, bắt đầu phiên khám với bác sĩ.',
  COMPLETED: 'Kết thúc lượt khám lâm sàng tại phòng khám.',
  SKIPPED: 'Tạm đánh dấu qua lượt do bệnh nhân vắng mặt khi gọi số.',
  WAITING: 'Đưa bệnh nhân về lại danh sách chờ tại điểm dịch vụ.',
};

const PRESENCE_OPTIONS: readonly { value: QueuePresenceStatus; label: string }[] = [
  { value: 'PRESENT', label: 'Có mặt tại điểm khám' },
  { value: 'NOT_ARRIVED', label: 'Chưa đến' },
  { value: 'LEFT', label: 'Tạm rời khu vực chờ' },
];

const SKIPPED_REASON_SUGGESTIONS: readonly string[] = [
  'Gọi 3 lần chưa có mặt tại cửa phòng khám',
  'Bệnh nhân xin hoãn lượt tạm thời',
];

export function UpdateQueueStatusModal({
  open,
  entry,
  currentQueueData,
  onClose,
  onUpdated,
}: UpdateQueueStatusModalProps) {
  const allowedStatuses = entry ? getAllowedNextQueueStatuses(entry.status) : [];

  const [targetStatus, setTargetStatus] = useState<ExaminationQueueStatus>(
    () => allowedStatuses[0] ?? entry?.status ?? 'WAITING',
  );
  const [presenceStatus, setPresenceStatus] = useState<QueuePresenceStatus>(
    () => entry?.presenceStatus ?? 'PRESENT',
  );
  const [reason, setReason] = useState<string>(() => entry?.blockedReason ?? '');
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);

  const mutation = useUpdateExaminationQueueStatus();

  if (!open || !entry) {
    return null;
  }

  const currentStatusMeta = EXAMINATION_QUEUE_STATUS_META[entry.status];
  const currentPresenceMeta = PRESENCE_STATUS_META[entry.presenceStatus];

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!entry) return;
    setApiError(null);

    const parsed = updateQueueStatusSchema.safeParse({
      queueEntryId: entry.id,
      currentStatus: entry.status,
      targetStatus,
      presenceStatus,
      reason,
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

    const payload: UpdateExaminationQueueStatusPayload = {
      targetStatus: parsed.data.targetStatus,
      presenceStatus: parsed.data.presenceStatus,
      reason: parsed.data.reason.length > 0 ? parsed.data.reason : null,
    };

    try {
      const result = await mutation.mutateAsync({
        queueEntryId: parsed.data.queueEntryId,
        payload,
        currentQueueData,
      });
      onUpdated(result);
    } catch (error) {
      setApiError(getErrorMessage(error, 'Không thể cập nhật trạng thái hàng đợi khám.'));
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="update-queue-status-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6 space-y-5"
      >
        <div className="flex items-start justify-between gap-4 pr-6">
          <div>
            <div className="flex items-center gap-2">
              <ArrowRightLeft className="h-5 w-5 text-primary" aria-hidden="true" />
              <h2 id="update-queue-status-modal-title" className="text-lg font-bold text-navy">
                Cập nhật trạng thái hàng đợi khám (UC28)
              </h2>
            </div>
            <p className="mt-1 text-xs text-sub">
              Chuyển tiếp trạng thái lượt khám theo quy trình điều phối tại điểm dịch vụ lâm sàng.
            </p>
          </div>
          <Badge tone="sky">Điều phối hàng đợi</Badge>
        </div>

        {apiError && (
          <div role="alert">
            <Alert tone="error">{apiError}</Alert>
          </div>
        )}

        {/* Patient & Queue Context Card */}
        <div className="rounded-xl border border-border bg-canvas p-4 grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Số phiếu & Thứ tự
            </div>
            <div className="mt-1 flex items-center gap-2">
              <span className="font-mono text-base font-extrabold text-navy">{entry.ticketNo}</span>
              <span className="rounded-md bg-white border border-border px-2 py-0.5 font-mono text-xs font-bold text-navy">
                #{entry.servingOrder}
              </span>
            </div>
            <div className="mt-0.5 font-mono text-[11px] text-sub">{entry.visitCode}</div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bệnh nhân
            </div>
            <div className="mt-1 text-sm font-bold text-ink">{entry.patientName}</div>
            <div className="font-mono text-xs text-sub">{entry.patientCode}</div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Điểm dịch vụ & Trạng thái hiện tại
            </div>
            <div className="mt-1 flex items-center gap-1.5 text-xs font-semibold text-navy">
              <DoorOpen className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span className="font-mono font-bold">{entry.roomCode}</span>
              <span className="truncate">{entry.roomName}</span>
            </div>
            <div className="mt-0.5 flex items-center gap-1 text-[11px] text-sub">
              <Stethoscope className="h-3 w-3 text-primary" aria-hidden="true" />
              <span className="truncate">{entry.doctorName}</span>
            </div>
            <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
              <Badge tone={currentStatusMeta.tone}>{currentStatusMeta.label}</Badge>
              <Badge tone={currentPresenceMeta.tone}>{currentPresenceMeta.label}</Badge>
            </div>
          </div>
        </div>

        {allowedStatuses.length === 0 ? (
          <Alert tone="info">
            Lượt khám này đã hoàn tất. Không còn chuyển tiếp trạng thái tiếp theo.
          </Alert>
        ) : (
          <>
            {/* Target Status Selector */}
            <div className="space-y-2">
              <label className="block text-xs font-semibold uppercase tracking-wide text-navy">
                Trạng thái chuyển tiếp
              </label>
              <div
                role="radiogroup"
                aria-label="Chọn trạng thái hàng đợi mới"
                className="grid grid-cols-1 sm:grid-cols-2 gap-2.5"
              >
                {allowedStatuses.map((statusOption) => {
                  const meta = EXAMINATION_QUEUE_STATUS_META[statusOption];
                  const isSelected = targetStatus === statusOption;

                  return (
                    <button
                      key={statusOption}
                      type="button"
                      role="radio"
                      aria-checked={isSelected}
                      onClick={() => {
                        setTargetStatus(statusOption);
                        setErrors((prev) => ({
                          ...prev,
                          targetStatus: undefined,
                          reason: undefined,
                        }));
                        setApiError(null);
                      }}
                      className={`flex flex-col justify-between rounded-xl border p-3.5 text-left transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
                        isSelected
                          ? 'border-primary bg-primary/5 ring-1 ring-primary'
                          : 'border-border bg-white hover:border-primary/50 hover:bg-canvas/60'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <Badge tone={meta.tone}>{meta.label}</Badge>
                        {isSelected && (
                          <CheckCircle2
                            className="h-4 w-4 text-primary shrink-0"
                            aria-hidden="true"
                          />
                        )}
                      </div>
                      <p className="mt-2 text-xs text-sub leading-relaxed">
                        {STATUS_TRANSITION_DESCRIPTIONS[statusOption]}
                      </p>
                    </button>
                  );
                })}
              </div>
              {errors.targetStatus && (
                <p className="text-xs font-medium text-rose-600">{errors.targetStatus}</p>
              )}
            </div>

            {/* Presence Status Selector */}
            <div className="space-y-1.5">
              <label
                htmlFor="update-queue-presence-status"
                className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-navy"
              >
                <UserCheck className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                <span>Trạng thái hiện diện</span>
              </label>
              <select
                id="update-queue-presence-status"
                aria-label="Trạng thái hiện diện"
                value={presenceStatus}
                onChange={(e) => {
                  setPresenceStatus(e.target.value as QueuePresenceStatus);
                  setErrors((prev) => ({ ...prev, presenceStatus: undefined }));
                  setApiError(null);
                }}
                className="w-full rounded-lg border border-border bg-white px-3.5 py-2.5 text-xs font-medium text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
              >
                {PRESENCE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
              {errors.presenceStatus && (
                <p className="text-xs font-medium text-rose-600">{errors.presenceStatus}</p>
              )}
            </div>

            {/* Reason / Note Input */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between gap-2">
                <label
                  htmlFor="update-queue-status-reason"
                  className="text-xs font-semibold uppercase tracking-wide text-navy"
                >
                  Lý do / Ghi chú điều phối{' '}
                  {targetStatus === 'SKIPPED' && <span className="text-rose-600">*</span>}
                </label>
                <span className="text-[11px] text-sub">
                  {targetStatus === 'SKIPPED'
                    ? 'Bắt buộc khi chọn Qua lượt'
                    : 'Tùy chọn (tối đa 500 ký tự)'}
                </span>
              </div>

              {targetStatus === 'SKIPPED' && (
                <div className="flex flex-wrap items-center gap-1.5 pb-1">
                  {SKIPPED_REASON_SUGGESTIONS.map((suggestion) => (
                    <button
                      key={suggestion}
                      type="button"
                      onClick={() => {
                        setReason(suggestion);
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

              <textarea
                id="update-queue-status-reason"
                aria-label="Lý do hoặc ghi chú cập nhật trạng thái"
                rows={3}
                value={reason}
                placeholder={
                  targetStatus === 'SKIPPED'
                    ? 'Nhập lý do bệnh nhân qua lượt (ví dụ: Gọi 3 lần chưa có mặt tại cửa phòng khám)...'
                    : 'Nhập ghi chú điều phối hàng đợi nếu có...'
                }
                onChange={(e) => {
                  setReason(e.target.value);
                  setErrors((prev) => ({ ...prev, reason: undefined }));
                  setApiError(null);
                }}
                className="w-full rounded-lg border border-border bg-white px-3.5 py-2.5 text-xs text-ink shadow-sm placeholder:text-sub/60 focus:outline-none focus:ring-2 focus:ring-primary/40"
              />
              {errors.reason && (
                <p className="text-xs font-medium text-rose-600">{errors.reason}</p>
              )}
            </div>
          </>
        )}

        <div className="flex items-center justify-end gap-2.5 border-t border-border pt-4">
          <Button type="button" variant="secondary" onClick={onClose}>
            Hủy
          </Button>
          <Button
            type="submit"
            variant="primary"
            loading={mutation.isPending}
            disabled={allowedStatuses.length === 0}
          >
            Xác nhận cập nhật
          </Button>
        </div>
      </form>
    </Modal>
  );
}
