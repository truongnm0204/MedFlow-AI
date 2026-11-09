import { useState, type FormEvent } from 'react';
import axios from 'axios';
import { AlertCircle, FileCheck2, Stethoscope, X } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { useCreateReturnToDoctorQueueEntry } from '../hooks/use-examination-queue';
import { useQueueEntryStatusDetail } from '../hooks/use-queue-status-update';
import {
  returnToDoctorQueueSchema,
  type ReturnToDoctorQueueFormValues,
} from '../schemas/return-to-doctor-queue.schema';
import type {
  CreateReturnToDoctorQueueEntryRequestPayload,
  CreateReturnToDoctorQueueEntryResponseDto,
  ReturnToDoctorQueueTarget,
} from '../types/examination-queue.types';
import {
  EXAMINATION_QUEUE_PRIORITY_META,
  EXAMINATION_QUEUE_STATUS_META,
  PRESENCE_STATUS_META,
} from './examination-queue-service-point-card';

export interface ReturnToDoctorQueueModalProps {
  open: boolean;
  target: ReturnToDoctorQueueTarget;
  onClose: () => void;
  onApplied: (result: CreateReturnToDoctorQueueEntryResponseDto) => void;
}

type FieldErrors = Partial<Record<keyof ReturnToDoctorQueueFormValues, string>>;

const CREATE_RETURN_REASON_PRESETS = [
  'Bệnh nhân đã hoàn tất cận lâm sàng và quay lại phòng khám đọc kết quả',
  'Đăng ký lượt quay lại phòng khám bác sĩ trong khi chờ trả kết quả cận lâm sàng',
  'Tiếp nhận bệnh nhân quay lại buồng khám theo chỉ định của bác sĩ',
] as const;

const ACTIVATE_RETURN_REASON_PRESETS = [
  'Đã có đầy đủ kết quả cận lâm sàng bắt buộc, bệnh nhân có mặt sẵn sàng đọc kết quả',
  'Xác nhận hoàn tất kết quả CLS và bệnh nhân đã có mặt trước phòng khám',
  'Kích hoạt lượt khám lại sau khi kiểm tra kết quả CLS và hiện diện tại quầy',
] as const;

export function ReturnToDoctorQueueModal({
  open,
  target,
  onClose,
  onApplied,
}: ReturnToDoctorQueueModalProps) {
  const { entry, servicePoint, mode } = target;
  const isActivateMode = mode === 'ACTIVATE_RETURN';

  const [values, setValues] = useState<ReturnToDoctorQueueFormValues>(() => ({
    mode,
    servicePointId: servicePoint.servicePointId,
    diagnosticResultsReady: !isActivateMode,
    confirmPresent: !isActivateMode,
    reason: '',
    notes: '',
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusDetailQuery = useQueueEntryStatusDetail(entry.id, open);
  const returnToDoctorMutation = useCreateReturnToDoctorQueueEntry();
  const stateVersion = statusDetailQuery.data?.stateVersion;

  if (!open) {
    return null;
  }

  const statusMeta = EXAMINATION_QUEUE_STATUS_META[entry.status];
  const presenceMeta = PRESENCE_STATUS_META[entry.presenceStatus];
  const priorityMeta = EXAMINATION_QUEUE_PRIORITY_META[entry.priority];
  const reasonPresets = isActivateMode
    ? ACTIVATE_RETURN_REASON_PRESETS
    : CREATE_RETURN_REASON_PRESETS;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = returnToDoctorQueueSchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof ReturnToDoctorQueueFormValues | undefined;
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
      const payload: CreateReturnToDoctorQueueEntryRequestPayload = {
        servicePointId: parsed.data.servicePointId,
        confirmPresent: parsed.data.confirmPresent,
        diagnosticResultsReady: parsed.data.diagnosticResultsReady,
        reason: parsed.data.reason,
        notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
        expectedStateVersion: stateVersion,
      };
      const response = await returnToDoctorMutation.mutateAsync({
        idOrCode: entry.id,
        payload,
      });
      onApplied(response);
    } catch (error) {
      setApiError(
        getErrorMessage(
          error,
          isActivateMode
            ? 'Không thể kích hoạt lượt khám lại đọc kết quả CLS.'
            : 'Không thể tạo lượt khám lại đọc kết quả CLS.',
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
        aria-labelledby="return-to-doctor-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6 space-y-4"
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-4 border-b border-border pb-4">
          <div>
            <h2
              id="return-to-doctor-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              <FileCheck2 aria-hidden="true" className="h-5 w-5 text-primary" />
              <span>
                {isActivateMode
                  ? 'Kích hoạt lượt khám lại đọc kết quả CLS (WAITING → READY)'
                  : 'Tiếp nhận bệnh nhân quay lại đọc kết quả CLS (RETURN_CONSULTATION)'}
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
            aria-label="Đóng hộp thoại tiếp nhận quay lại đọc kết quả CLS"
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
              {isActivateMode ? (
                <Badge tone="emerald">Giữ nguyên số phiếu #{entry.queueNumber}</Badge>
              ) : (
                <Badge tone="sky">Hoàn tất lượt khám đầu &amp; cấp số phiếu khám lại mới</Badge>
              )}
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bệnh nhân &amp; Ưu tiên lâm sàng
            </div>
            <div className="mt-0.5 text-sm font-bold text-ink">{entry.patientName}</div>
            <div className="font-mono text-xs text-sub">{entry.patientCode}</div>
            <div className="mt-1.5 flex flex-wrap items-center gap-1">
              <Badge tone="emerald">Đọc KQ CLS</Badge>
              <Badge tone={priorityMeta.tone}>Ưu tiên: {priorityMeta.label}</Badge>
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Trạng thái &amp; Điều kiện CLS
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-1">
              <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
              <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
              {entry.ineligibleReason === 'AWAITING_RESULTS' && (
                <Badge tone="rose">Chờ kết quả CLS</Badge>
              )}
              {entry.queueType === 'RETURN_CONSULTATION' &&
                entry.ineligibleReason !== 'AWAITING_RESULTS' && (
                  <Badge tone="emerald">Đủ KQ CLS</Badge>
                )}
            </div>
            <div className="mt-1.5 font-mono text-xs text-sub space-y-0.5">
              <div>Check-in: {entry.checkedInAt}</div>
              {entry.calledAt && <div>Đã gọi lúc: {entry.calledAt}</div>}
            </div>
          </div>
        </div>

        {/* Coordination Policy Banner */}
        <Alert tone="info">
          <div className="font-semibold">
            Quy tắc điều phối lượt quay lại đọc kết quả cận lâm sàng (UC28 — RETURN_CONSULTATION)
          </div>
          <ul className="mt-1 list-disc list-inside space-y-1 text-xs">
            <li>
              Lượt quay lại có{' '}
              <span className="font-mono font-bold">queueType = RETURN_CONSULTATION</span>, tách
              biệt khỏi mức ưu tiên lâm sàng (<span className="font-mono">NORMAL</span>,{' '}
              <span className="font-mono">ELDERLY</span>,{' '}
              <span className="font-mono">PREGNANT</span>).
            </li>
            <li>
              Chỉ đủ điều kiện tham gia xếp thứ tự gọi khám (<strong>READY</strong> /{' '}
              <strong>ELIGIBLE</strong>) khi toàn bộ kết quả cận lâm sàng bắt buộc (
              <span className="font-mono">final_result_required</span>) đã sẵn sàng{' '}
              <strong>và</strong> bệnh nhân đã có mặt trở lại (
              <span className="font-mono">presenceStatus = PRESENT</span>).
            </li>
            <li>
              Khi đủ điều kiện, Backend tự động tính lại{' '}
              <span className="font-mono font-bold">servingOrder</span> theo cơ chế công bằng (
              <span className="font-mono">walkin_priority_ratio</span>) và bảo vệ lịch hẹn đúng giờ
              (<strong>Appointment Protection</strong>).
            </li>
          </ul>
        </Alert>

        {/* Readiness & Presence Checkboxes */}
        <div className="space-y-3 rounded-xl border border-border bg-canvas p-4">
          <div className="text-xs font-semibold text-navy">
            Xác nhận điều kiện sẵn sàng đọc kết quả cận lâm sàng
          </div>

          <div>
            <div className="flex items-start gap-2.5">
              <input
                type="checkbox"
                id="return-to-doctor-results-ready"
                checked={values.diagnosticResultsReady}
                onChange={(e) => {
                  setValues((prev) => ({
                    ...prev,
                    diagnosticResultsReady: e.target.checked,
                  }));
                  setErrors((prev) => ({ ...prev, diagnosticResultsReady: undefined }));
                  setApiError(null);
                }}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary/40"
              />
              <label
                htmlFor="return-to-doctor-results-ready"
                className="text-xs font-medium text-ink leading-relaxed"
              >
                Đã có đầy đủ kết quả cận lâm sàng bắt buộc (final_result_required)
              </label>
            </div>
            {errors.diagnosticResultsReady && (
              <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
                {errors.diagnosticResultsReady}
              </p>
            )}
          </div>

          <div>
            <div className="flex items-start gap-2.5">
              <input
                type="checkbox"
                id="return-to-doctor-confirm-present"
                checked={values.confirmPresent}
                onChange={(e) => {
                  setValues((prev) => ({ ...prev, confirmPresent: e.target.checked }));
                  setErrors((prev) => ({ ...prev, confirmPresent: undefined }));
                  setApiError(null);
                }}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary/40"
              />
              <label
                htmlFor="return-to-doctor-confirm-present"
                className="text-xs font-medium text-ink leading-relaxed"
              >
                Tôi xác nhận bệnh nhân đã có mặt trực tiếp tại khu vực phòng khám (presenceStatus =
                PRESENT)
              </label>
            </div>
            {errors.confirmPresent && (
              <p role="alert" className="mt-1.5 text-xs font-semibold text-rose-600">
                {errors.confirmPresent}
              </p>
            )}
          </div>

          <div
            data-testid="return-to-doctor-outcome-preview"
            className="pt-1 border-t border-border flex flex-wrap items-center gap-2"
          >
            {values.diagnosticResultsReady && values.confirmPresent ? (
              <Badge tone="emerald">
                Trạng thái sau lưu: Sẵn sàng (READY) • Đủ điều kiện xếp thứ tự gọi khám
              </Badge>
            ) : (
              <Badge tone="amber">
                Trạng thái sau lưu: Đang chờ (WAITING) • Chưa đủ điều kiện gọi (Chờ KQ CLS / Chưa có
                mặt)
              </Badge>
            )}
          </div>
        </div>

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
                  aria-pressed={isSelected}
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
            id="return-to-doctor-reason"
            label="Lý do điều phối lượt khám lại (bắt buộc)"
            placeholder={
              isActivateMode
                ? 'Nhập lý do kích hoạt lượt khám lại sau khi đủ kết quả CLS...'
                : 'Nhập lý do tiếp nhận bệnh nhân quay lại đọc kết quả cận lâm sàng...'
            }
            maxLength={300}
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
            id="return-to-doctor-notes"
            label="Ghi chú bổ sung (tùy chọn)"
            placeholder="Ghi chú thêm về các chỉ định CLS hoặc hướng dẫn bác sĩ..."
            maxLength={300}
            value={values.notes}
            error={errors.notes}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, notes: e.target.value }));
              setErrors((prev) => ({ ...prev, notes: undefined }));
              setApiError(null);
            }}
          />
        </div>

        {statusDetailQuery.isPending && (
          <div role="status" className="text-xs text-sub">
            Đang đồng bộ phiên bản trạng thái lượt khám...
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
            disabled={isSubmitting || stateVersion === undefined}
          >
            {isActivateMode
              ? 'Xác nhận kích hoạt lượt khám lại'
              : 'Xác nhận tiếp nhận lượt khám lại'}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
