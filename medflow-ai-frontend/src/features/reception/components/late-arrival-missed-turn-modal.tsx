import { useState, type FormEvent } from 'react';
import axios from 'axios';
import { AlertCircle, Clock, RotateCcw, Stethoscope, UserX, X } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { useMarkMissedTurn, useRestoreSkippedTurn } from '../hooks/use-examination-queue';
import { useQueueEntryStatusDetail } from '../hooks/use-queue-status-update';
import {
  lateArrivalMissedTurnSchema,
  type LateArrivalMissedTurnFormValues,
} from '../schemas/late-arrival-missed-turn.schema';
import type {
  LateArrivalMissedTurnTarget,
  MarkMissedTurnRequestPayload,
  QueueTurnActionResponseDto,
  RestoreQueueTurnRequestPayload,
} from '../types/examination-queue.types';
import {
  ARRIVAL_CLASS_META,
  EXAMINATION_QUEUE_STATUS_META,
  PRESENCE_STATUS_META,
} from './examination-queue-service-point-card';

export interface LateArrivalMissedTurnModalProps {
  open: boolean;
  target: LateArrivalMissedTurnTarget;
  onClose: () => void;
  onApplied: (result: QueueTurnActionResponseDto) => void;
}

type FieldErrors = Partial<Record<keyof LateArrivalMissedTurnFormValues, string>>;

const MISSED_TURN_REASON_PRESETS = [
  'Gọi 3 lần chưa có mặt tại cửa phòng khám',
  'Bệnh nhân tạm rời khu vực chờ khi bác sĩ gọi số',
  'Bệnh nhân đang bổ sung thủ tục hành chính tại quầy',
] as const;

const RESTORE_TURN_REASON_PRESETS = [
  'Bệnh nhân đã có mặt trở lại tại phòng khám, xác nhận sẵn sàng khám',
  'Bệnh nhân đến muộn sau khi gọi số, đã kiểm tra hiện diện tại quầy',
  'Khôi phục lượt khám sau khi hoàn tất thủ tục bổ sung',
] as const;

export function LateArrivalMissedTurnModal({
  open,
  target,
  onClose,
  onApplied,
}: LateArrivalMissedTurnModalProps) {
  const { entry, servicePoint, mode } = target;
  const isRestoreMode = mode === 'RESTORE_TURN';

  const [values, setValues] = useState<LateArrivalMissedTurnFormValues>(() => ({
    mode,
    servicePointId: servicePoint.servicePointId,
    confirmPresent: false,
    reason: '',
    notes: '',
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusDetailQuery = useQueueEntryStatusDetail(entry.id, open);
  const markMissedMutation = useMarkMissedTurn();
  const restoreTurnMutation = useRestoreSkippedTurn();
  const stateVersion = statusDetailQuery.data?.stateVersion;

  if (!open) {
    return null;
  }

  const statusMeta = EXAMINATION_QUEUE_STATUS_META[entry.status];
  const presenceMeta = PRESENCE_STATUS_META[entry.presenceStatus];
  const arrivalMeta = ARRIVAL_CLASS_META[entry.arrivalClass];
  const reasonPresets = isRestoreMode ? RESTORE_TURN_REASON_PRESETS : MISSED_TURN_REASON_PRESETS;
  const isLateScheduled =
    entry.visitType === 'SCHEDULED' &&
    (entry.arrivalClass === 'LATE' || entry.isAppointmentProtected === false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = lateArrivalMissedTurnSchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof LateArrivalMissedTurnFormValues | undefined;
        if (field && !nextErrors[field]) {
          nextErrors[field] = issue.message;
        }
      }
      setErrors(nextErrors);
      return;
    }

    if (stateVersion === undefined) {
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    try {
      if (isRestoreMode) {
        const payload: RestoreQueueTurnRequestPayload = {
          servicePointId: parsed.data.servicePointId,
          confirmPresent: parsed.data.confirmPresent,
          reason: parsed.data.reason,
          notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
          expectedStateVersion: stateVersion,
        };
        const response = await restoreTurnMutation.mutateAsync({
          idOrCode: entry.id,
          payload,
        });
        onApplied(response);
      } else {
        const payload: MarkMissedTurnRequestPayload = {
          servicePointId: parsed.data.servicePointId,
          reason: parsed.data.reason,
          notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
          expectedStateVersion: stateVersion,
        };
        const response = await markMissedMutation.mutateAsync({
          idOrCode: entry.id,
          payload,
        });
        onApplied(response);
      }
    } catch (error) {
      setApiError(
        getErrorMessage(
          error,
          isRestoreMode
            ? 'Không thể khôi phục lượt khám cho bệnh nhân qua lượt.'
            : 'Không thể đánh dấu qua lượt cho bệnh nhân vắng mặt.',
        ),
      );
      if (axios.isAxiosError(error) && error.response?.status === 409) {
        void statusDetailQuery.refetch();
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="late-arrival-missed-turn-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6 space-y-4"
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-4 border-b border-border pb-4">
          <div>
            <h2
              id="late-arrival-missed-turn-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              {isRestoreMode ? (
                <RotateCcw aria-hidden="true" className="h-5 w-5 text-primary" />
              ) : (
                <UserX aria-hidden="true" className="h-5 w-5 text-rose-600" />
              )}
              <span>
                {isRestoreMode
                  ? 'Xử lý bệnh nhân đến muộn & Khôi phục lượt khám (SKIPPED → READY)'
                  : 'Đánh dấu bệnh nhân qua lượt khi gọi khám (CALLED → SKIPPED)'}
              </span>
            </h2>
            <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-sub">
              <span className="font-mono font-semibold text-navy">{servicePoint.roomCode}</span>
              <span>•</span>
              <span>{servicePoint.roomName}</span>
              <span>•</span>
              <span className="inline-flex items-center gap-1">
                <Stethoscope aria-hidden="true" className="h-3.5 w-3.5 text-primary" />
                {servicePoint.doctorName}
              </span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại xử lý đến muộn và qua lượt"
            className="rounded-xl p-1.5 text-sub hover:bg-canvas hover:text-ink transition-colors"
          >
            <X aria-hidden="true" className="h-5 w-5" />
          </button>
        </div>

        {apiError && (
          <div
            role="alert"
            className="flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            <AlertCircle aria-hidden="true" className="h-4 w-4 shrink-0" />
            <span>{apiError}</span>
          </div>
        )}

        {/* Context Summary Grid */}
        <div className="grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Số phiếu &amp; Thứ tự hiện tại
            </div>
            <div className="mt-0.5 flex items-center gap-2">
              <span className="font-mono text-base font-extrabold text-navy">{entry.ticketNo}</span>
              <span className="rounded-md bg-white px-2 py-0.5 font-mono text-xs font-bold text-navy border border-border">
                #{entry.servingOrder}
              </span>
            </div>
            <div className="font-mono text-xs text-sub">{entry.visitCode}</div>
            <div className="mt-1.5">
              <Badge tone="emerald">Giữ nguyên số phiếu #{entry.queueNumber}</Badge>
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bệnh nhân
            </div>
            <div className="mt-0.5 text-sm font-bold text-ink">{entry.patientName}</div>
            <div className="font-mono text-xs text-sub">{entry.patientCode}</div>
            <div className="mt-1.5 flex flex-wrap items-center gap-1">
              <Badge tone={entry.visitType === 'SCHEDULED' ? 'sky' : 'slate'}>
                {entry.visitType === 'SCHEDULED' ? 'Hẹn trước' : 'Walk-in'}
              </Badge>
              <Badge tone={arrivalMeta.tone}>{arrivalMeta.label}</Badge>
              {isLateScheduled && <Badge tone="amber">Mất bảo vệ giờ hẹn</Badge>}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Trạng thái &amp; Mốc giờ
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-1">
              <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
              <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
            </div>
            <div className="mt-1.5 font-mono text-xs text-sub space-y-0.5">
              {entry.scheduledAt && <div>Giờ hẹn: {entry.scheduledAt}</div>}
              <div>Check-in: {entry.checkedInAt}</div>
              {entry.calledAt && <div>Đã gọi lúc: {entry.calledAt}</div>}
            </div>
          </div>
        </div>

        {/* Policy Banner */}
        {isRestoreMode ? (
          <Alert tone={isLateScheduled ? 'warning' : 'info'}>
            <div className="font-semibold">
              {isLateScheduled
                ? 'Bệnh nhân hẹn trước đến muộn / quay lại ngoài thời gian ân hạn (LATE — mất quyền bảo vệ khung giờ hẹn cũ)'
                : 'Quy tắc khôi phục lượt khám (SKIPPED → READY)'}
            </div>
            <p className="mt-1 text-xs">
              Khi xác nhận bệnh nhân đã có mặt trực tiếp (<strong>PRESENT</strong>), hệ thống chuyển
              trạng thái từ <strong>Qua lượt (SKIPPED)</strong> sang{' '}
              <strong>Sẵn sàng (READY)</strong>, giữ nguyên số phiếu{' '}
              <span className="font-mono font-bold">#{entry.queueNumber}</span> và tự động tính lại{' '}
              <span className="font-mono font-bold">servingOrder</span> từ thời điểm có mặt lại theo
              chính sách điều phối của Backend.
            </p>
            {entry.blockedReason && (
              <div className="mt-1.5 flex items-center gap-1.5 font-mono text-[11px] text-rose-700">
                <Clock aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
                <span>Lý do qua lượt trước đó: {entry.blockedReason}</span>
              </div>
            )}
          </Alert>
        ) : (
          <Alert tone="warning">
            <div className="font-semibold">
              Quy tắc đánh dấu Qua lượt khi gọi khám vắng mặt (CALLED → SKIPPED)
            </div>
            <p className="mt-1 text-xs">
              Lượt khám sẽ chuyển sang trạng thái <strong>Qua lượt (SKIPPED)</strong> và hiện diện{' '}
              <strong>Vắng mặt (LEFT)</strong> để nhường quyền gọi bệnh nhân kế tiếp. Số phiếu{' '}
              <span className="font-mono font-bold">#{entry.queueNumber}</span> được giữ nguyên để
              khôi phục khi bệnh nhân quay lại quầy.
            </p>
          </Alert>
        )}

        {/* Preset Reasons */}
        <div className="space-y-2">
          <div className="text-xs font-semibold text-navy">Gợi ý lý do điều phối nhanh</div>
          <div className="flex flex-wrap gap-1.5">
            {reasonPresets.map((preset) => {
              const isSelected = values.reason === preset;
              return (
                <button
                  key={preset}
                  type="button"
                  onClick={() => {
                    setValues((prev) => ({ ...prev, reason: preset }));
                    setErrors((prev) => ({ ...prev, reason: undefined }));
                    setApiError(null);
                  }}
                  className={`rounded-lg border px-2.5 py-1.5 text-left text-xs font-medium transition-colors ${
                    isSelected
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-white text-sub hover:bg-canvas hover:text-ink'
                  }`}
                >
                  {preset}
                </button>
              );
            })}
          </div>
        </div>

        {/* Reason Input */}
        <div>
          <Input
            id="queue-turn-action-reason"
            label="Lý do điều phối lượt khám (bắt buộc, 5–300 ký tự)"
            placeholder={
              isRestoreMode
                ? 'Nhập lý do xác nhận bệnh nhân có mặt trở lại để khôi phục lượt...'
                : 'Nhập lý do đánh dấu bệnh nhân qua lượt khi gọi số...'
            }
            value={values.reason}
            error={errors.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
              setApiError(null);
            }}
          />
        </div>

        {/* Notes Input */}
        <div>
          <Input
            id="queue-turn-action-notes"
            label="Ghi chú bổ sung (tùy chọn, tối đa 300 ký tự)"
            placeholder="Ghi chú thêm về tình trạng tiếp nhận tại quầy hoặc cửa buồng khám..."
            value={values.notes}
            error={errors.notes}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, notes: e.target.value }));
              setErrors((prev) => ({ ...prev, notes: undefined }));
              setApiError(null);
            }}
          />
        </div>

        {/* Confirm Present Checkbox (RESTORE_TURN only) */}
        {isRestoreMode && (
          <div>
            <div className="flex items-start gap-2.5 rounded-xl border border-border bg-canvas p-3.5">
              <input
                type="checkbox"
                id="restore-turn-confirm-present"
                checked={values.confirmPresent}
                onChange={(e) => {
                  setValues((prev) => ({ ...prev, confirmPresent: e.target.checked }));
                  setErrors((prev) => ({ ...prev, confirmPresent: undefined }));
                  setApiError(null);
                }}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary/40"
              />
              <label
                htmlFor="restore-turn-confirm-present"
                className="text-xs font-medium text-ink leading-relaxed"
              >
                Tôi xác nhận bệnh nhân đã có mặt trực tiếp (<strong>PRESENT</strong>) tại quầy tiếp
                đón / cửa phòng khám và sẵn sàng chờ gọi khám lại
              </label>
            </div>
            {errors.confirmPresent && (
              <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
                {errors.confirmPresent}
              </p>
            )}
          </div>
        )}

        {statusDetailQuery.isPending && (
          <div role="status" className="text-xs text-sub">
            Đang tải phiên bản trạng thái lượt xếp hàng...
          </div>
        )}

        {statusDetailQuery.isError && (
          <div
            role="alert"
            className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            {`Không tải được phiên bản trạng thái hiện tại của lượt xếp hàng: ${getErrorMessage(
              statusDetailQuery.error,
              'Lỗi không xác định',
            )}`}
          </div>
        )}

        {/* Footer Actions */}
        <div className="flex justify-end gap-3 pt-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button
            type="submit"
            variant="primary"
            loading={isSubmitting}
            disabled={stateVersion === undefined}
          >
            {isRestoreMode ? 'Xác nhận khôi phục lượt khám' : 'Xác nhận đánh dấu qua lượt'}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
