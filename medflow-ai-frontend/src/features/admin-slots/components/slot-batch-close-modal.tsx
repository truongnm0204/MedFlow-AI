import { useState } from 'react';
import { AlertTriangle, Ban, Info, ShieldAlert } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { CLOSE_SLOT_REASONS, slotCloseSchema } from '../schemas/examination-slot.schema';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type {
  AffectedBookingsAction,
  CloseSlotReasonCode,
  ExaminationSlot,
} from '../types/examination-slot.types';

interface SlotBatchCloseModalProps {
  slots: ExaminationSlot[];
  open: boolean;
  onClose: () => void;
  onSuccess?: (count: number, message: string) => void;
}

export function SlotBatchCloseModal({ slots, open, onClose, onSuccess }: SlotBatchCloseModalProps) {
  const batchCloseSlots = useExaminationSlotStore((state) => state.batchCloseSlots);

  const [reasonCode, setReasonCode] = useState<CloseSlotReasonCode>('SCHEDULE_ADJUSTMENT');
  const [note, setNote] = useState('');
  const [notifyDoctor, setNotifyDoctor] = useState(true);
  const [notifyPatients, setNotifyPatients] = useState(true);
  const [affectedBookingsAction, setAffectedBookingsAction] =
    useState<AffectedBookingsAction>('KEEP_EXISTING');
  const [confirmUnderstood, setConfirmUnderstood] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  if (!open || slots.length === 0) return null;

  const totalBookings = slots.reduce((acc, s) => acc + s.capacityBooked, 0);
  const affectedSlots = slots.filter((s) => s.capacityBooked > 0);
  const hasBookings = totalBookings > 0;
  const selectedReasonObj = CLOSE_SLOT_REASONS.find((r) => r.code === reasonCode);
  const isSubmitDisabled = isSubmitting || (hasBookings && !confirmUnderstood);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);
    setValidationError(null);

    const parseResult = slotCloseSchema.safeParse({
      reasonCode,
      note,
      notifyDoctor,
      notifyPatients,
      affectedBookingsAction,
      confirmUnderstood,
    });

    if (!parseResult.success) {
      const firstIssue = parseResult.error.issues[0];
      setValidationError(firstIssue?.message ?? 'Vui lòng kiểm tra lại thông tin nhập.');
      return;
    }

    if (hasBookings && !confirmUnderstood) {
      setValidationError(
        `Vui lòng tích chọn xác nhận đã kiểm tra tổng cộng ${totalBookings} lượt đặt trước khi đóng hàng loạt.`,
      );
      return;
    }

    const targetIds = slots.map((s) => s.id);
    setIsSubmitting(true);
    try {
      const res = await batchCloseSlots(targetIds, {
        reasonCode,
        reasonText: selectedReasonObj?.label ?? reasonCode,
        note: note.trim() || undefined,
        notifyDoctor,
        notifyPatients,
        affectedBookingsAction,
      });
      onSuccess?.(res.count, `Đã đóng thành công ${res.count} khung giờ khám được chọn.`);
      onClose();
    } catch (err) {
      setErrorMessage(
        err instanceof Error
          ? err.message
          : 'Không thể đóng hàng loạt khung giờ khám. Vui lòng thử lại.',
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-xl">
      <form onSubmit={handleSubmit} noValidate className="p-6 sm:p-7">
        {/* Header */}
        <div className="flex items-start gap-3.5 border-b border-border pb-4 pr-8">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
            <Ban className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-ink">Đóng Hàng Loạt Khung Giờ Khám</h2>
            <p className="mt-0.5 text-xs text-sub">
              Xác nhận đóng <b>{slots.length}</b> khung giờ đã chọn trên toàn bộ hệ thống.
            </p>
          </div>
        </div>

        {/* Selected Slots Summary Box */}
        <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
          <div className="flex items-center justify-between text-xs">
            <span className="text-sub font-medium">Số lượng khung giờ đã chọn:</span>
            <span className="font-bold text-ink">{slots.length} khung giờ</span>
          </div>
          <div className="mt-2 flex items-center justify-between text-xs">
            <span className="text-sub font-medium">Tổng lượt bệnh nhân đã đặt:</span>
            <span
              className={`font-mono font-bold ${hasBookings ? 'text-amber-700' : 'text-slate-600'}`}
            >
              {totalBookings} lượt ({affectedSlots.length} khung giờ có lịch)
            </span>
          </div>

          {/* Quick list of slots */}
          <div className="mt-3 max-h-24 overflow-y-auto rounded-lg border border-slate-200/80 bg-white p-2 text-[11px] divide-y divide-slate-100">
            {slots.map((s) => (
              <div key={s.id} className="py-1 flex items-center justify-between gap-2">
                <span className="font-mono font-semibold text-slate-800">{s.code}</span>
                <span className="truncate text-sub">
                  {s.doctorName} · {s.dateDisplay} {s.startTime}
                </span>
                <span className="shrink-0 font-mono text-slate-600">
                  {s.capacityBooked}/{s.capacityMax} chỗ
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Error / Validation Alert */}
        {(errorMessage || validationError) && (
          <div className="mt-4 flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800">
            <AlertTriangle className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">Không thể hoàn tất đóng hàng loạt:</p>
              <p className="mt-0.5">{errorMessage || validationError}</p>
            </div>
          </div>
        )}

        {/* Bookings Warning */}
        {hasBookings ? (
          <div className="mt-4 rounded-xl border border-amber-300 bg-amber-50/80 p-4">
            <div className="flex items-start gap-2.5">
              <ShieldAlert className="h-5 w-5 shrink-0 text-amber-600 mt-0.5" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <h3 className="text-xs font-bold uppercase tracking-wider text-amber-900">
                  Cảnh báo: Có tổng cộng {totalBookings} lượt đặt trong {affectedSlots.length} khung
                  giờ!
                </h3>
                <p className="mt-1 text-xs text-amber-800 leading-relaxed">
                  Đóng các khung giờ này sẽ ngừng nhận đặt khám mới. Vui lòng xác định phương án xử
                  lý cho {totalBookings} lượt khám đã đặt trước.
                </p>

                <div className="mt-3 space-y-2">
                  <label className="flex items-start gap-2 text-xs text-amber-950 font-medium cursor-pointer">
                    <input
                      type="radio"
                      name="batchAffectedBookingsAction"
                      value="KEEP_EXISTING"
                      checked={affectedBookingsAction === 'KEEP_EXISTING'}
                      onChange={() => setAffectedBookingsAction('KEEP_EXISTING')}
                      className="mt-0.5 text-amber-600 focus:ring-amber-500"
                    />
                    <span>
                      Giữ nguyên lượt hẹn đã đặt — Bác sĩ phụ trách vẫn tiếp tục khám theo lịch
                    </span>
                  </label>
                  <label className="flex items-start gap-2 text-xs text-amber-950 font-medium cursor-pointer">
                    <input
                      type="radio"
                      name="batchAffectedBookingsAction"
                      value="RESCHEDULE_REQUIRED"
                      checked={affectedBookingsAction === 'RESCHEDULE_REQUIRED'}
                      onChange={() => setAffectedBookingsAction('RESCHEDULE_REQUIRED')}
                      className="mt-0.5 text-amber-600 focus:ring-amber-500"
                    />
                    <span>
                      Yêu cầu liên hệ dời lịch — Bộ phận tiếp đón sẽ liên hệ bệnh nhân sắp xếp lại
                    </span>
                  </label>
                </div>

                <div className="mt-3.5 border-t border-amber-200/80 pt-3">
                  <label className="flex items-start gap-2 text-xs text-amber-950 font-medium cursor-pointer">
                    <input
                      type="checkbox"
                      id="batch-confirm-understood-checkbox"
                      checked={confirmUnderstood}
                      onChange={(e) => setConfirmUnderstood(e.target.checked)}
                      className="mt-0.5 rounded border-amber-400 text-rose-600 focus:ring-rose-500"
                    />
                    <span>
                      Tôi đã nắm rõ số lượng <b>{totalBookings}</b> lượt đặt trước và xác nhận đóng
                      hàng loạt <b>{slots.length}</b> khung giờ này.
                    </span>
                  </label>
                </div>
              </div>
            </div>
          </div>
        ) : (
          <div className="mt-4 flex items-start gap-2.5 rounded-lg border border-sky-200 bg-sky-50/80 p-3.5 text-xs text-sky-800">
            <Info className="h-4 w-4 shrink-0 text-sky-600 mt-0.5" aria-hidden="true" />
            <div>
              <p className="font-semibold">
                Tất cả {slots.length} khung giờ đã chọn đều chưa có lượt đặt
              </p>
              <p className="mt-0.5 text-sky-700">
                Việc đóng hàng loạt sẽ không ảnh hưởng tới bất kỳ bệnh nhân nào.
              </p>
            </div>
          </div>
        )}

        {/* Reason and Details */}
        <div className="mt-4 space-y-3.5">
          <div>
            <label
              htmlFor="batch-close-reason-select"
              className="block text-xs font-semibold text-ink mb-1"
            >
              Lý do đóng hàng loạt <span className="text-rose-500">*</span>
            </label>
            <select
              id="batch-close-reason-select"
              value={reasonCode}
              onChange={(e) => setReasonCode(e.target.value as CloseSlotReasonCode)}
              className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
            >
              {CLOSE_SLOT_REASONS.map((reason) => (
                <option key={reason.code} value={reason.code}>
                  {reason.label}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label
              htmlFor="batch-close-note-textarea"
              className="block text-xs font-semibold text-ink mb-1"
            >
              Ghi chú bổ sung {reasonCode === 'OTHER' && <span className="text-rose-500">*</span>}
            </label>
            <textarea
              id="batch-close-note-textarea"
              rows={2}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder={
                reasonCode === 'OTHER'
                  ? 'Vui lòng nhập chi tiết lý do đóng khung giờ (tối thiểu 5 ký tự)...'
                  : 'Ghi chú thêm cho điều phối viên / tiếp nhận (tùy chọn)...'
              }
              className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
            />
          </div>

          {/* Notification Checkboxes */}
          <div className="rounded-lg border border-slate-200 bg-slate-50/50 p-3 space-y-2 text-xs">
            <label className="flex items-center gap-2 text-ink cursor-pointer">
              <input
                type="checkbox"
                checked={notifyDoctor}
                onChange={(e) => setNotifyDoctor(e.target.checked)}
                className="rounded border-slate-300 text-primary focus:ring-primary"
              />
              <span>Gửi thông báo cập nhật ca trực tới tất cả bác sĩ phụ trách</span>
            </label>

            {hasBookings && (
              <label className="flex items-center gap-2 text-ink cursor-pointer">
                <input
                  type="checkbox"
                  checked={notifyPatients}
                  onChange={(e) => setNotifyPatients(e.target.checked)}
                  className="rounded border-slate-300 text-primary focus:ring-primary"
                />
                <span>
                  Gửi SMS / Email thông báo điều chỉnh tới {totalBookings} bệnh nhân đã đặt
                </span>
              </label>
            )}
          </div>
        </div>

        {/* Footer Actions */}
        <div className="mt-6 flex items-center justify-end gap-2.5 border-t border-border pt-4">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={onClose}
            disabled={isSubmitting}
          >
            Hủy bỏ
          </Button>
          <Button
            type="submit"
            variant="danger"
            size="sm"
            loading={isSubmitting}
            disabled={isSubmitDisabled}
          >
            <Ban className="h-3.5 w-3.5 mr-1" aria-hidden="true" />
            Xác nhận đóng {slots.length} khung giờ
          </Button>
        </div>
      </form>
    </Modal>
  );
}
