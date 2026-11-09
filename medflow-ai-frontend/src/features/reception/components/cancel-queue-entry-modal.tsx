import { useState, type FormEvent } from 'react';
import axios from 'axios';
import { AlertCircle, AlertTriangle, XCircle } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { useCancelQueueEntry, useQueueEntryStatusDetail } from '../hooks/use-queue-status-update';
import {
  cancelQueueEntrySchema,
  type CancelQueueEntryFormValues,
} from '../schemas/cancel-queue-entry.schema';
import type {
  CancelQueueEntryRequestPayload,
  CancelQueueEntryResponseDto,
  CancelQueueEntryTarget,
} from '../types/queue-status-update.types';
import {
  EXAMINATION_QUEUE_STATUS_META,
  PRESENCE_STATUS_META,
} from './examination-queue-service-point-card';

export interface CancelQueueEntryModalProps {
  open: boolean;
  target: CancelQueueEntryTarget;
  onClose: () => void;
  onCancelled: (result: CancelQueueEntryResponseDto) => void;
}

const REASON_MAX_LENGTH = 500;

type FieldErrors = Partial<Record<keyof CancelQueueEntryFormValues, string>>;

export function CancelQueueEntryModal({
  open,
  target,
  onClose,
  onCancelled,
}: CancelQueueEntryModalProps) {
  const { entry, servicePoint } = target;
  const [values, setValues] = useState<CancelQueueEntryFormValues>({ reason: '', notes: '' });
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusDetailQuery = useQueueEntryStatusDetail(entry.id, open);
  const cancelMutation = useCancelQueueEntry();
  const stateVersion = statusDetailQuery.data?.stateVersion;

  if (!open) {
    return null;
  }

  const statusMeta = EXAMINATION_QUEUE_STATUS_META[entry.status];
  const presenceMeta = PRESENCE_STATUS_META[entry.presenceStatus];
  const isReturnConsultation = entry.queueType === 'RETURN_CONSULTATION';

  function handleChange(field: keyof CancelQueueEntryFormValues, value: string) {
    setValues((prev) => ({ ...prev, [field]: value }));
    setErrors((prev) => ({ ...prev, [field]: undefined }));
    setApiError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = cancelQueueEntrySchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof CancelQueueEntryFormValues | undefined;
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

    const payload: CancelQueueEntryRequestPayload = {
      reason: parsed.data.reason,
      expectedStateVersion: stateVersion,
      notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
    };

    try {
      const response = await cancelMutation.mutateAsync({ idOrCode: entry.id, payload });
      onCancelled(response);
    } catch (error) {
      setApiError(getErrorMessage(error, 'Không thể hủy lượt xếp hàng.'));
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
        aria-labelledby="cancel-queue-entry-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <div>
          <h2
            id="cancel-queue-entry-modal-title"
            className="flex items-center gap-2 text-lg font-bold text-navy"
          >
            <XCircle aria-hidden="true" className="h-5 w-5 text-rose-600" />
            <span>Hủy lượt khám trong hàng đợi</span>
          </h2>
          <p className="mt-0.5 text-xs text-sub">
            Dùng khi bệnh nhân rời đi hoặc rút lui trước khi bắt đầu khám. Thứ tự phục vụ của các
            lượt còn lại do hệ thống tính lại sau khi hủy.
          </p>
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
                {entry.ticketNo}
              </span>
              <span className="text-sm font-bold text-ink">{entry.patientName}</span>
            </div>
            <div className="font-mono text-xs text-sub">
              {entry.patientCode} • STT #{entry.queueNumber} • Thứ tự #{entry.servingOrder}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Buồng khám
            </div>
            <div className="mt-0.5 text-sm font-semibold text-ink">
              <span className="font-mono">{servicePoint.roomCode}</span> — {servicePoint.roomName}
            </div>
            <div className="text-xs text-sub">{servicePoint.doctorName}</div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Trạng thái hiện tại
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
              <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
              {isReturnConsultation && <Badge tone="emerald">Đọc KQ CLS</Badge>}
            </div>
          </div>
        </div>

        <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800">
          <div className="flex items-center gap-2 font-semibold">
            <AlertTriangle aria-hidden="true" className="h-4 w-4 shrink-0" />
            <span>Thao tác hủy lượt không thể hoàn tác</span>
          </div>
          <ul className="mt-1.5 list-disc space-y-0.5 pl-6">
            <li>Số phiếu #{entry.queueNumber} được giữ nguyên và không cấp lại cho lượt khác.</li>
            <li>
              {isReturnConsultation
                ? 'Chỉ hủy lượt quay lại đọc kết quả CLS; lượt khám (visit) vẫn mở.'
                : 'Lượt khám (visit) gắn với phiếu này sẽ chuyển sang Đã hủy.'}
            </li>
            <li>Lịch hẹn không bị thay đổi (hủy lịch hẹn thực hiện ở chức năng riêng).</li>
          </ul>
        </div>

        <div className="mt-4">
          <Input
            id="cancel-queue-entry-reason"
            label="Lý do hủy lượt (bắt buộc)"
            placeholder="Ví dụ: Bệnh nhân xin về, không tiếp tục khám"
            maxLength={REASON_MAX_LENGTH}
            value={values.reason}
            error={errors.reason}
            onChange={(e) => handleChange('reason', e.target.value)}
          />
          <div className="mt-1 text-right font-mono text-[11px] text-sub">
            {values.reason.trim().length}/{REASON_MAX_LENGTH}
          </div>
        </div>

        <div className="mt-2">
          <Input
            id="cancel-queue-entry-notes"
            label="Ghi chú (không bắt buộc)"
            maxLength={REASON_MAX_LENGTH}
            value={values.notes}
            error={errors.notes}
            onChange={(e) => handleChange('notes', e.target.value)}
          />
        </div>

        {statusDetailQuery.isPending && (
          <div role="status" className="mt-4 text-xs text-sub">
            Đang tải phiên bản trạng thái lượt xếp hàng...
          </div>
        )}

        {statusDetailQuery.isError && (
          <div
            role="alert"
            className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            <span>
              {`Không tải được phiên bản trạng thái hiện tại của lượt xếp hàng: ${getErrorMessage(
                statusDetailQuery.error,
                'Lỗi không xác định',
              )}`}
            </span>
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => void statusDetailQuery.refetch()}
            >
              Thử lại
            </Button>
          </div>
        )}

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" onClick={onClose}>
            Quay lại
          </Button>
          <Button
            type="submit"
            variant="danger"
            loading={isSubmitting}
            disabled={stateVersion === undefined}
          >
            Xác nhận hủy lượt
          </Button>
        </div>
      </form>
    </Modal>
  );
}
