import { useState, type FormEvent } from 'react';
import axios from 'axios';
import { AlertCircle, ShieldCheck } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import {
  useQueueEntryStatusDetail,
  useUpdateQueuePriority,
} from '../hooks/use-queue-status-update';
import {
  applyClinicalPrioritySchema,
  type ApplyClinicalPriorityFormValues,
} from '../schemas/apply-clinical-priority.schema';
import type {
  ExaminationQueueEntryDto,
  ExaminationQueuePriority,
} from '../types/examination-queue.types';
import type {
  UpdateQueuePriorityRequestPayload,
  UpdateQueuePriorityResponseDto,
} from '../types/queue-status-update.types';
import { EXAMINATION_QUEUE_PRIORITY_META } from './examination-queue-service-point-card';

export interface ApplyClinicalPriorityModalProps {
  open: boolean;
  entry: ExaminationQueueEntryDto;
  onClose: () => void;
  onApplied: (result: UpdateQueuePriorityResponseDto) => void;
}

const PRIORITY_OPTIONS: ReadonlyArray<{
  value: ExaminationQueuePriority;
  label: string;
}> = [
  { value: 'NORMAL', label: 'Bình thường (NORMAL)' },
  { value: 'ELDERLY', label: 'Người cao tuổi (ELDERLY)' },
  { value: 'PREGNANT', label: 'Thai phụ (PREGNANT)' },
];

type FieldErrors = Partial<Record<keyof ApplyClinicalPriorityFormValues, string>>;

export function ApplyClinicalPriorityModal({
  open,
  entry,
  onClose,
  onApplied,
}: ApplyClinicalPriorityModalProps) {
  const [values, setValues] = useState<ApplyClinicalPriorityFormValues>(() => ({
    priority: entry.priority,
    reason: '',
    verificationConfirmed: false,
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusDetailQuery = useQueueEntryStatusDetail(entry.id, open);
  const updatePriorityMutation = useUpdateQueuePriority();
  const stateVersion = statusDetailQuery.data?.stateVersion;

  if (!open) {
    return null;
  }

  const currentPriorityMeta = EXAMINATION_QUEUE_PRIORITY_META[entry.priority];

  function handleSelectPriority(nextPriority: ExaminationQueuePriority) {
    setValues((prev) => ({
      ...prev,
      priority: nextPriority,
      verificationConfirmed: false,
    }));
    setErrors((prev) => ({
      ...prev,
      priority: undefined,
      verificationConfirmed: undefined,
    }));
    setApiError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = applyClinicalPrioritySchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof ApplyClinicalPriorityFormValues | undefined;
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

    const payload: UpdateQueuePriorityRequestPayload = {
      priority: parsed.data.priority,
      reason: parsed.data.reason,
      verificationConfirmed: parsed.data.priority !== 'NORMAL' && parsed.data.verificationConfirmed,
      expectedStateVersion: stateVersion,
    };

    try {
      const response = await updatePriorityMutation.mutateAsync({
        idOrCode: entry.id,
        payload,
      });
      onApplied(response);
    } catch (error) {
      setApiError(getErrorMessage(error, 'Không thể áp dụng mức ưu tiên cho lượt xếp hàng.'));
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
        aria-labelledby="apply-clinical-priority-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2
              id="apply-clinical-priority-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              <ShieldCheck aria-hidden="true" className="h-5 w-5 text-primary" />
              <span>Áp dụng ưu tiên khám ngoại trú</span>
            </h2>
            <p className="mt-0.5 text-xs text-sub">
              Chỉ áp dụng cho đối tượng ưu tiên theo quy định (người cao tuổi từ 75 tuổi, phụ nữ
              mang thai). Không áp dụng cấp cứu. Thứ tự phục vụ do hệ thống tính lại sau khi lưu; số
              phiếu giữ nguyên.
            </p>
          </div>
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
            <div className="font-mono text-xs text-sub">{entry.patientCode}</div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Buồng khám
            </div>
            <div className="mt-0.5 text-sm font-semibold text-ink">
              {entry.roomCode} — {entry.roomName}
            </div>
            <div className="text-xs text-sub">{entry.departmentName}</div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Ưu tiên hiện tại
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              <Badge tone={currentPriorityMeta.tone}>{currentPriorityMeta.label}</Badge>
              {entry.queueType === 'RETURN_CONSULTATION' && (
                <Badge tone="emerald">Đọc KQ CLS</Badge>
              )}
            </div>
          </div>
        </div>

        <div className="mt-5">
          <div className="text-xs font-semibold uppercase tracking-wide text-navy">
            Chọn mức ưu tiên khám ngoại trú
          </div>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            {PRIORITY_OPTIONS.map((option) => {
              const isSelected = values.priority === option.value;
              return (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => handleSelectPriority(option.value)}
                  className={`rounded-xl border px-3.5 py-2 text-xs font-semibold transition-colors ${
                    isSelected
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-white text-ink hover:bg-canvas'
                  }`}
                >
                  {option.label}
                </button>
              );
            })}
          </div>
          {errors.priority && (
            <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
              {errors.priority}
            </p>
          )}
        </div>

        <div className="mt-4">
          <Input
            id="apply-priority-reason"
            label="Lý do xác minh đối tượng ưu tiên (bắt buộc)"
            placeholder="Ví dụ: Đã kiểm tra CCCD, bệnh nhân sinh năm 1945"
            value={values.reason}
            error={errors.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
              setApiError(null);
            }}
          />
        </div>

        {values.priority !== 'NORMAL' && (
          <div className="mt-4">
            <div className="flex items-start gap-2.5">
              <input
                type="checkbox"
                id="apply-priority-verification"
                checked={values.verificationConfirmed}
                onChange={(e) => {
                  setValues((prev) => ({ ...prev, verificationConfirmed: e.target.checked }));
                  setErrors((prev) => ({ ...prev, verificationConfirmed: undefined }));
                  setApiError(null);
                }}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary/40"
              />
              <label
                htmlFor="apply-priority-verification"
                className="text-xs font-medium text-ink leading-relaxed"
              >
                Tôi đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên (CCCD/giấy tờ tùy thân hoặc sổ
                khám thai)
              </label>
            </div>
            {errors.verificationConfirmed && (
              <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
                {errors.verificationConfirmed}
              </p>
            )}
          </div>
        )}

        {statusDetailQuery.isPending && (
          <div role="status" className="mt-4 text-xs text-sub">
            Đang tải phiên bản trạng thái lượt xếp hàng...
          </div>
        )}

        {statusDetailQuery.isError && (
          <div
            role="alert"
            className="mt-4 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            {`Không tải được phiên bản trạng thái hiện tại của lượt xếp hàng: ${getErrorMessage(
              statusDetailQuery.error,
              'Lỗi không xác định',
            )}`}
          </div>
        )}

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button type="submit" loading={isSubmitting} disabled={stateVersion === undefined}>
            Xác nhận áp dụng ưu tiên
          </Button>
        </div>
      </form>
    </Modal>
  );
}
