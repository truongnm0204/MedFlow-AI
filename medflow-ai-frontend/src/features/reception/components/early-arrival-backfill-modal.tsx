import { useMemo, useState, type FormEvent } from 'react';
import { Clock, ShieldCheck, Sparkles, Stethoscope, X } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { useApplyEarlyArrivalBackfill } from '../hooks/use-examination-queue';
import {
  earlyArrivalBackfillSchema,
  type EarlyArrivalBackfillFormValues,
} from '../schemas/early-arrival-backfill.schema';
import type {
  EarlyArrivalBackfillActionMode,
  EarlyArrivalBackfillRequestPayload,
  EarlyArrivalBackfillResponseDto,
  EarlyArrivalBackfillTarget,
  QueueArrivalClass,
} from '../types/examination-queue.types';
import { evaluateSafeBackfillEligibility } from '../utils/early-arrival-backfill.utils';

export interface EarlyArrivalBackfillModalProps {
  open: boolean;
  target: EarlyArrivalBackfillTarget;
  onClose: () => void;
  onApplied: (result: EarlyArrivalBackfillResponseDto) => void;
  onSubmitOverride?: (
    queueEntryId: string,
    payload: EarlyArrivalBackfillRequestPayload,
  ) => Promise<EarlyArrivalBackfillResponseDto>;
}

type FieldErrors = Partial<Record<keyof EarlyArrivalBackfillFormValues, string>>;

const REASON_PRESETS = [
  'Bệnh nhân đến sớm, phòng khám đang trống lượt hẹn đúng giờ',
  'Tận dụng khoảng trống giữa các ca hẹn để Safe Backfill',
  'Giữ chờ tới khung giờ hẹn để bảo vệ lượt khám đúng giờ',
] as const;

export function EarlyArrivalBackfillModal({
  open,
  target,
  onClose,
  onApplied,
  onSubmitOverride,
}: EarlyArrivalBackfillModalProps) {
  const evaluation = useMemo(
    () => evaluateSafeBackfillEligibility(target.entry, target.servicePoint),
    [target.entry, target.servicePoint],
  );

  const [values, setValues] = useState<EarlyArrivalBackfillFormValues>(() => ({
    queueEntryId: target.entry.id,
    servicePointId: target.servicePoint.servicePointId,
    visitType: target.entry.visitType,
    actionMode: evaluation.canSafeBackfill ? 'SAFE_BACKFILL' : 'HOLD_FOR_SLOT',
    arrivalClass:
      target.entry.visitType === 'WALK_IN'
        ? 'WALK_IN'
        : target.entry.arrivalClass === 'EARLY'
          ? 'EARLY'
          : 'EARLY',
    reason: target.entry.backfillReason ?? '',
    notes: '',
    acknowledgeAppointmentProtection: evaluation.canSafeBackfill,
  }));

  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const backfillMutation = useApplyEarlyArrivalBackfill();

  const handleActionModeChange = (mode: EarlyArrivalBackfillActionMode) => {
    if (mode === 'SAFE_BACKFILL' && !evaluation.canSafeBackfill) {
      return;
    }
    setValues((prev) => ({
      ...prev,
      actionMode: mode,
    }));
    setErrors((prev) => ({ ...prev, actionMode: undefined }));
    setApiError(null);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setApiError(null);

    const parsed = earlyArrivalBackfillSchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof EarlyArrivalBackfillFormValues | undefined;
        if (field && !nextErrors[field]) {
          nextErrors[field] = issue.message;
        }
      }
      setErrors(nextErrors);
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    const payload: EarlyArrivalBackfillRequestPayload = {
      servicePointId: parsed.data.servicePointId,
      actionMode: parsed.data.actionMode,
      arrivalClass: parsed.data.arrivalClass,
      reason: parsed.data.reason,
      notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
      acknowledgeAppointmentProtection: parsed.data.acknowledgeAppointmentProtection,
    };

    try {
      const response = onSubmitOverride
        ? await onSubmitOverride(parsed.data.queueEntryId, payload)
        : await backfillMutation.mutateAsync({
            queueEntryId: parsed.data.queueEntryId,
            payload,
          });

      onApplied(response);
    } catch (error) {
      setApiError(
        getErrorMessage(error, 'Không thể xử lý điều phối đến sớm / Safe Backfill cho lượt khám.'),
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="early-arrival-backfill-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6 space-y-4"
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-4 border-b border-border pb-4">
          <div>
            <h2
              id="early-arrival-backfill-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              <Sparkles aria-hidden="true" className="h-5 w-5 text-primary" />
              <span>Xử lý bệnh nhân đến sớm &amp; Safe Backfill (UC28)</span>
            </h2>
            <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-sub">
              <span className="font-mono font-semibold text-navy">
                {target.servicePoint.roomCode}
              </span>
              <span>•</span>
              <span>{target.servicePoint.roomName}</span>
              <span>•</span>
              <span className="inline-flex items-center gap-1">
                <Stethoscope aria-hidden="true" className="h-3.5 w-3.5 text-primary" />
                {target.servicePoint.doctorName}
              </span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại xử lý đến sớm và Safe Backfill"
            className="rounded-xl p-1.5 text-sub hover:bg-canvas hover:text-ink transition-colors"
          >
            <X aria-hidden="true" className="h-5 w-5" />
          </button>
        </div>

        {/* Context Summary Grid (3 columns) */}
        <div className="grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Số phiếu &amp; Mã lượt
            </div>
            <div className="mt-0.5 font-mono text-base font-extrabold text-navy">
              {target.entry.ticketNo}
            </div>
            <div className="font-mono text-xs text-sub">{target.entry.visitCode}</div>
            <div className="mt-1.5">
              <Badge tone="emerald">Giữ nguyên số phiếu #{target.entry.queueNumber}</Badge>
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bệnh nhân
            </div>
            <div className="mt-0.5 text-sm font-bold text-ink">{target.entry.patientName}</div>
            <div className="font-mono text-xs text-sub">{target.entry.patientCode}</div>
            <div className="mt-1.5">
              <Badge tone={target.entry.visitType === 'SCHEDULED' ? 'sky' : 'slate'}>
                {target.entry.visitType === 'SCHEDULED' ? 'Hẹn trước' : 'Walk-in'}
              </Badge>
            </div>
          </div>

          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Mốc thời gian
            </div>
            <div className="mt-0.5 font-mono text-xs font-semibold text-ink">
              {target.entry.scheduledAt
                ? `Giờ hẹn: ${target.entry.scheduledAt}`
                : 'Không có lịch hẹn (Walk-in)'}
            </div>
            <div className="font-mono text-xs text-sub">Check-in: {target.entry.checkedInAt}</div>
            {evaluation.earlyLeadMinutes !== null && evaluation.earlyLeadMinutes > 0 && (
              <div className="mt-1.5">
                <Badge tone="sky">
                  <Clock aria-hidden="true" className="mr-1 h-3 w-3" />
                  Đến sớm {evaluation.earlyLeadMinutes} phút
                </Badge>
              </div>
            )}
          </div>
        </div>

        {/* Appointment Protection & Room Gap Banner */}
        {evaluation.appointmentProtectionBlocked ? (
          <Alert tone="warning">
            <div className="font-semibold">{evaluation.statusReason}</div>
            {evaluation.protectedAppointments.length > 0 && (
              <ul className="mt-1.5 list-disc pl-4 space-y-0.5 font-mono text-[11px]">
                {evaluation.protectedAppointments.map((p) => (
                  <li key={p.id}>
                    {p.ticketNo} • {p.patientName} (Hẹn {p.scheduledAt ?? '—'})
                  </li>
                ))}
              </ul>
            )}
          </Alert>
        ) : evaluation.canSafeBackfill ? (
          <Alert tone="success">{evaluation.statusReason}</Alert>
        ) : (
          <Alert tone="info">{evaluation.statusReason}</Alert>
        )}

        {/* Action Mode Radio Cards */}
        <div className="space-y-2">
          <div className="text-xs font-semibold text-navy">Chế độ điều phối hàng đợi</div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label
              className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3.5 text-left transition-all ${
                values.actionMode === 'SAFE_BACKFILL'
                  ? 'border-primary bg-primary/5 ring-2 ring-primary/15'
                  : 'border-border bg-white hover:bg-canvas/60'
              } ${!evaluation.canSafeBackfill ? 'cursor-not-allowed opacity-60' : ''}`}
            >
              <input
                type="radio"
                name="actionMode"
                value="SAFE_BACKFILL"
                checked={values.actionMode === 'SAFE_BACKFILL'}
                disabled={!evaluation.canSafeBackfill}
                onChange={() => handleActionModeChange('SAFE_BACKFILL')}
                className="mt-0.5 h-4 w-4 text-primary focus:ring-primary"
              />
              <div className="min-w-0 flex-1">
                <div className="text-xs font-bold text-navy">
                  Chèn khám sớm an toàn (Safe Backfill)
                </div>
                <div className="mt-1 text-[11px] leading-relaxed text-sub">
                  Chuyển trạng thái sang Sẵn sàng (READY) để tận dụng thời gian trống của phòng khám
                  mà không đổi số phiếu.
                </div>
              </div>
            </label>

            <label
              className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3.5 text-left transition-all ${
                values.actionMode === 'HOLD_FOR_SLOT'
                  ? 'border-primary bg-primary/5 ring-2 ring-primary/15'
                  : 'border-border bg-white hover:bg-canvas/60'
              }`}
            >
              <input
                type="radio"
                name="actionMode"
                value="HOLD_FOR_SLOT"
                checked={values.actionMode === 'HOLD_FOR_SLOT'}
                onChange={() => handleActionModeChange('HOLD_FOR_SLOT')}
                className="mt-0.5 h-4 w-4 text-primary focus:ring-primary"
              />
              <div className="min-w-0 flex-1">
                <div className="text-xs font-bold text-navy">
                  Giữ chờ theo khung giờ hẹn (Hold for Slot)
                </div>
                <div className="mt-1 text-[11px] leading-relaxed text-sub">
                  Ghi nhận bệnh nhân đến sớm (EARLY), giữ trạng thái Đang chờ (WAITING) và bảo vệ
                  thứ tự của các lịch hẹn đúng giờ.
                </div>
              </div>
            </label>
          </div>
          {errors.actionMode && (
            <p className="text-xs font-medium text-rose-600">{errors.actionMode}</p>
          )}
        </div>

        {/* Arrival Classification Select */}
        <div className="space-y-1.5">
          <label
            htmlFor="early-arrival-class-select"
            className="block text-xs font-semibold text-navy"
          >
            Phân loại thời điểm đến
          </label>
          <select
            id="early-arrival-class-select"
            aria-label="Phân loại thời điểm đến"
            value={values.arrivalClass}
            onChange={(e) => {
              setValues((prev) => ({
                ...prev,
                arrivalClass: e.target.value as QueueArrivalClass,
              }));
              setErrors((prev) => ({ ...prev, arrivalClass: undefined }));
              setApiError(null);
            }}
            className="w-full rounded-xl border border-border bg-white px-3.5 py-2.5 text-xs font-medium text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15"
          >
            <option value="EARLY">Đến sớm (EARLY)</option>
            <option value="ON_TIME_OR_GRACE">Đúng giờ / Trong thời gian ân hạn</option>
            <option value="WALK_IN">Vãng lai (WALK_IN)</option>
          </select>
        </div>

        {/* Quick Reason Presets & Reason Input */}
        <div className="space-y-2">
          <label
            htmlFor="early-arrival-reason-input"
            className="block text-xs font-semibold text-navy"
          >
            Lý do điều phối đến sớm / Safe Backfill <span className="text-rose-600">*</span>
          </label>
          <div className="flex flex-wrap gap-1.5">
            {REASON_PRESETS.map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => {
                  setValues((prev) => ({ ...prev, reason: preset }));
                  setErrors((prev) => ({ ...prev, reason: undefined }));
                  setApiError(null);
                }}
                className="rounded-lg border border-border bg-canvas px-2.5 py-1 text-[11px] font-medium text-sub hover:border-primary hover:text-primary transition-colors"
              >
                {preset}
              </button>
            ))}
          </div>
          <input
            id="early-arrival-reason-input"
            type="text"
            aria-label="Lý do điều phối đến sớm / Safe Backfill"
            value={values.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
              setApiError(null);
            }}
            placeholder="Nhập lý do điều phối đến sớm hoặc Safe Backfill..."
            className="w-full rounded-xl border border-border bg-white px-3.5 py-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15"
          />
          {errors.reason && <p className="text-xs font-medium text-rose-600">{errors.reason}</p>}
        </div>

        {/* Notes Input */}
        <div className="space-y-1.5">
          <label
            htmlFor="early-arrival-notes-input"
            className="block text-xs font-semibold text-navy"
          >
            Ghi chú bổ sung (tùy chọn)
          </label>
          <input
            id="early-arrival-notes-input"
            type="text"
            aria-label="Ghi chú bổ sung"
            value={values.notes}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, notes: e.target.value }));
              setErrors((prev) => ({ ...prev, notes: undefined }));
              setApiError(null);
            }}
            placeholder="Ghi chú thêm cho điều dưỡng / bác sĩ tại phòng khám..."
            className="w-full rounded-xl border border-border bg-white px-3.5 py-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/15"
          />
          {errors.notes && <p className="text-xs font-medium text-rose-600">{errors.notes}</p>}
        </div>

        {/* Appointment Protection Checkbox */}
        <div className="rounded-xl border border-border bg-canvas/70 p-3.5">
          <label
            htmlFor="acknowledge-appointment-protection"
            className="flex cursor-pointer items-start gap-2.5 text-xs text-ink"
          >
            <input
              id="acknowledge-appointment-protection"
              type="checkbox"
              checked={values.acknowledgeAppointmentProtection}
              onChange={(e) => {
                setValues((prev) => ({
                  ...prev,
                  acknowledgeAppointmentProtection: e.target.checked,
                }));
                setErrors((prev) => ({ ...prev, acknowledgeAppointmentProtection: undefined }));
                setApiError(null);
              }}
              className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
            />
            <span className="leading-relaxed">
              <ShieldCheck
                aria-hidden="true"
                className="mr-1 inline h-4 w-4 text-emerald-600 align-text-bottom"
              />
              Tôi xác nhận giữ nguyên số thứ tự đã cấp và không xâm phạm quyền ưu tiên của bệnh nhân
              đặt lịch đúng giờ đang có mặt.
            </span>
          </label>
          {errors.acknowledgeAppointmentProtection && (
            <p className="mt-1.5 text-xs font-medium text-rose-600">
              {errors.acknowledgeAppointmentProtection}
            </p>
          )}
        </div>

        {/* API Error Alert */}
        {apiError && (
          <div role="alert">
            <Alert tone="error">{apiError}</Alert>
          </div>
        )}

        {/* Footer Actions */}
        <div className="flex items-center justify-end gap-2.5 border-t border-border pt-4">
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Hủy
          </Button>
          <Button type="submit" size="sm" disabled={isSubmitting}>
            {isSubmitting ? 'Đang xử lý...' : 'Xác nhận điều phối'}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
