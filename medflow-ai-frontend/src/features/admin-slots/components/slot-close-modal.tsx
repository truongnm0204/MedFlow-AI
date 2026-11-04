import { useState } from 'react';
import {
  AlertTriangle,
  Ban,
  Calendar,
  Clock,
  Info,
  ShieldAlert,
  Stethoscope,
  Users,
} from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { CLOSE_SLOT_REASONS, slotCloseSchema } from '../schemas/examination-slot.schema';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type {
  AffectedBookingsAction,
  CloseSlotPayload,
  CloseSlotReasonCode,
  ExaminationSlot,
} from '../types/examination-slot.types';

interface SlotCloseModalProps {
  slot: ExaminationSlot | null;
  open: boolean;
  onClose: () => void;
  onSuccess?: (slot: ExaminationSlot, message: string) => void;
}

export function SlotCloseModal({ slot, open, onClose, onSuccess }: SlotCloseModalProps) {
  const closeSlot = useExaminationSlotStore((state) => state.closeSlot);

  const [reasonCode, setReasonCode] = useState<CloseSlotReasonCode>('DOCTOR_UNAVAILABLE');
  const [note, setNote] = useState('');
  const [notifyDoctor, setNotifyDoctor] = useState(true);
  const [notifyPatients, setNotifyPatients] = useState(true);
  const [affectedBookingsAction, setAffectedBookingsAction] =
    useState<AffectedBookingsAction>('KEEP_EXISTING');
  const [confirmUnderstood, setConfirmUnderstood] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);
  if (!open || !slot) return null;

  const hasBookings = slot.capacityBooked > 0;
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
        `Vui lòng tích chọn xác nhận đã kiểm tra ${slot.capacityBooked} lượt đặt phụ thuộc trước khi đóng khung giờ.`,
      );
      return;
    }

    const payload: CloseSlotPayload = {
      slotId: slot.id,
      reasonCode,
      reasonText: selectedReasonObj?.label ?? reasonCode,
      note: note.trim() || undefined,
      notifyDoctor,
      notifyPatients,
      affectedBookingsAction,
    };

    setIsSubmitting(true);
    try {
      const res = await closeSlot(slot.id, payload);
      onSuccess?.(slot, res.message);
      onClose();
    } catch (err) {
      setErrorMessage(
        err instanceof Error
          ? err.message
          : 'Không thể đóng khung giờ khám. Vui lòng kiểm tra lại kết nối mạng.',
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
            <h2 className="text-lg font-bold text-ink">Xác Nhận Đóng Khung Giờ Khám</h2>
            <p className="mt-0.5 text-xs text-sub">
              Khung giờ sẽ ngừng nhận lượt đặt mới trên toàn hệ thống đặt lịch trực tuyến.
            </p>
          </div>
        </div>

        {/* Slot Summary Card */}
        <div className="mt-4 rounded-xl border border-slate-200/90 bg-slate-50/70 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200/60 pb-3">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs font-bold text-slate-800">{slot.code}</span>
              <span className="inline-flex items-center rounded-md border border-slate-200 bg-white px-2 py-0.5 text-[11px] font-medium text-slate-700">
                {slot.serviceType === 'DIAGNOSTIC' ? 'Cận lâm sàng' : 'Ngoại trú'}
              </span>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="text-xs text-sub">Sức chứa:</span>
              <span
                className={`font-mono text-xs font-bold ${
                  hasBookings ? 'text-amber-700' : 'text-slate-600'
                }`}
              >
                {String(slot.capacityBooked).padStart(2, '0')} /{' '}
                {String(slot.capacityMax).padStart(2, '0')} lượt
              </span>
            </div>
          </div>

          <div className="mt-3 grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
            <div className="flex items-center gap-2 text-ink">
              <Stethoscope className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
              <span className="truncate font-medium">{slot.doctorName}</span>
            </div>
            <div className="flex items-center gap-2 text-ink">
              <Clock className="h-3.5 w-3.5 shrink-0 text-slate-500" aria-hidden="true" />
              <span>
                {slot.startTime} - {slot.endTime} ({slot.dateDisplay})
              </span>
            </div>
            <div className="flex items-center gap-2 text-sub sm:col-span-2">
              <Calendar className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
              <span className="truncate">
                {slot.serviceName} · Phòng {slot.room} · {slot.specialty}
              </span>
            </div>
          </div>
        </div>

        {/* Error / Validation Alert */}
        {(errorMessage || validationError) && (
          <div className="mt-4 flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800">
            <AlertTriangle className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">Không thể hoàn tất đóng khung giờ:</p>
              <p className="mt-0.5">{errorMessage || validationError}</p>
            </div>
          </div>
        )}

        {/* Dependent Bookings Warning (CRITICAL FOR ISSUE #215) */}
        {hasBookings ? (
          <div className="mt-4 rounded-xl border border-amber-300 bg-amber-50/80 p-4">
            <div className="flex items-start gap-2.5">
              <ShieldAlert className="h-5 w-5 shrink-0 text-amber-600 mt-0.5" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <h3 className="text-xs font-bold uppercase tracking-wider text-amber-900">
                  Cảnh báo: Khung giờ có {slot.capacityBooked} lượt bệnh nhân đã đặt hẹn!
                </h3>
                <p className="mt-1 text-xs text-amber-800 leading-relaxed">
                  Khung giờ hiện có <b>{slot.capacityBooked}</b> lượt đặt trước. Đóng slot sẽ ngăn
                  lượt đặt mới. Các lượt khám đã đặt trước cần phương án xử lý điều phối:
                </p>

                {/* Handling Strategy Radio Selection */}
                <div className="mt-3 space-y-2">
                  <label
                    className={`flex items-start gap-2.5 rounded-lg border p-2.5 text-xs transition-colors cursor-pointer ${
                      affectedBookingsAction === 'KEEP_EXISTING'
                        ? 'border-amber-400 bg-white font-medium text-amber-950 shadow-2xs'
                        : 'border-amber-200/70 bg-amber-50/50 text-amber-900 hover:bg-white/60'
                    }`}
                  >
                    <input
                      type="radio"
                      name="affectedBookingsAction"
                      value="KEEP_EXISTING"
                      checked={affectedBookingsAction === 'KEEP_EXISTING'}
                      onChange={() => setAffectedBookingsAction('KEEP_EXISTING')}
                      className="mt-0.5 text-amber-600 focus:ring-amber-500"
                    />
                    <div>
                      <span className="font-semibold">Giữ nguyên lượt hẹn đã đặt</span>
                      <p className="text-[11px] text-amber-800/90 font-normal">
                        Bác sĩ vẫn tiến hành khám cho {slot.capacityBooked} bệnh nhân đã đặt theo
                        giờ hẹn; chỉ khóa nhận thêm bệnh nhân mới.
                      </p>
                    </div>
                  </label>

                  <label
                    className={`flex items-start gap-2.5 rounded-lg border p-2.5 text-xs transition-colors cursor-pointer ${
                      affectedBookingsAction === 'RESCHEDULE_REQUIRED'
                        ? 'border-amber-400 bg-white font-medium text-amber-950 shadow-2xs'
                        : 'border-amber-200/70 bg-amber-50/50 text-amber-900 hover:bg-white/60'
                    }`}
                  >
                    <input
                      type="radio"
                      name="affectedBookingsAction"
                      value="RESCHEDULE_REQUIRED"
                      checked={affectedBookingsAction === 'RESCHEDULE_REQUIRED'}
                      onChange={() => setAffectedBookingsAction('RESCHEDULE_REQUIRED')}
                      className="mt-0.5 text-amber-600 focus:ring-amber-500"
                    />
                    <div>
                      <span className="font-semibold">Yêu cầu liên hệ dời lịch / chuyển kíp</span>
                      <p className="text-[11px] text-amber-800/90 font-normal">
                        Ghi nhận cờ chuyển ca để bộ phận tiếp đón chủ động liên hệ bệnh nhân dời
                        sang khung giờ hoặc bác sĩ phù hợp khác.
                      </p>
                    </div>
                  </label>
                </div>

                {/* Mandatory Acknowledgment Checkbox */}
                <div className="mt-3.5 border-t border-amber-200/80 pt-3">
                  <label className="flex items-start gap-2 text-xs text-amber-950 font-medium cursor-pointer">
                    <input
                      type="checkbox"
                      id="confirm-understood-checkbox"
                      checked={confirmUnderstood}
                      onChange={(e) => setConfirmUnderstood(e.target.checked)}
                      className="mt-0.5 rounded border-amber-400 text-rose-600 focus:ring-rose-500"
                    />
                    <span>
                      Tôi đã nắm rõ số lượng <b>{slot.capacityBooked}</b> lượt đặt trước và xác nhận
                      đóng khung giờ này.
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
                Khung giờ chưa có lượt đặt nào (0/{slot.capacityMax} chỗ)
              </p>
              <p className="mt-0.5 text-sky-700">
                Đóng khung giờ này sẽ ngay lập tức ngừng hiển thị trên cổng đặt lịch mà không làm
                ảnh hưởng tới bất kỳ bệnh nhân nào.
              </p>
            </div>
          </div>
        )}

        {/* Form Controls: Reason & Note */}
        <div className="mt-4 space-y-3.5">
          <div>
            <label
              htmlFor="close-reason-select"
              className="block text-xs font-semibold text-ink mb-1"
            >
              Lý do đóng khung giờ <span className="text-rose-500">*</span>
            </label>
            <select
              id="close-reason-select"
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
              htmlFor="close-note-textarea"
              className="block text-xs font-semibold text-ink mb-1"
            >
              Ghi chú bổ sung {reasonCode === 'OTHER' && <span className="text-rose-500">*</span>}
            </label>
            <textarea
              id="close-note-textarea"
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
              <span className="flex items-center gap-1.5">
                <Stethoscope className="h-3.5 w-3.5 text-slate-500" aria-hidden="true" />
                Gửi thông báo cập nhật lịch tới Bác sĩ ({slot.doctorName})
              </span>
            </label>

            {hasBookings && (
              <label className="flex items-center gap-2 text-ink cursor-pointer">
                <input
                  type="checkbox"
                  checked={notifyPatients}
                  onChange={(e) => setNotifyPatients(e.target.checked)}
                  className="rounded border-slate-300 text-primary focus:ring-primary"
                />
                <span className="flex items-center gap-1.5">
                  <Users className="h-3.5 w-3.5 text-slate-500" aria-hidden="true" />
                  Gửi SMS / Email thông báo tới {slot.capacityBooked} bệnh nhân đã đặt
                </span>
              </label>
            )}
          </div>
        </div>

        {/* Modal Footer Actions */}
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
            Xác nhận đóng khung giờ
          </Button>
        </div>
      </form>
    </Modal>
  );
}
