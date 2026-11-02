import { useState } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Building2,
  CalendarClock,
  CheckCircle2,
  UserRound,
} from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import { getProposedRescheduleSlots } from '../api/appointment-detail.api';
import { rescheduleAppointmentSchema } from '../schemas/appointment-detail.schema';
import type { AppointmentListItem } from '../types/appointment-list.types';

interface AppointmentRescheduleModalProps {
  appointment: AppointmentListItem | null;
  isSubmitting?: boolean;
  onClose: () => void;
  onConfirmReschedule: (payload: { targetSlotId: string; rescheduleNote?: string }) => void;
}

export function AppointmentRescheduleModal({
  appointment,
  isSubmitting = false,
  onClose,
  onConfirmReschedule,
}: AppointmentRescheduleModalProps) {
  const proposedSlots = appointment ? getProposedRescheduleSlots(appointment) : [];
  const [selectedSlotId, setSelectedSlotId] = useState<string>(
    () => proposedSlots[0]?.slotId ?? 'slot-2026-1016-0830',
  );
  const [rescheduleNote, setRescheduleNote] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  if (!appointment) return null;

  const activeSlot =
    proposedSlots.find((s) => s.slotId === selectedSlotId) ?? proposedSlots[0] ?? null;

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = rescheduleAppointmentSchema.safeParse({
      targetSlotId: selectedSlotId,
      rescheduleNote,
    });

    if (!parsed.success) {
      setValidationError(parsed.error.issues[0]?.message ?? 'Dữ liệu đổi lịch không hợp lệ.');
      return;
    }

    setValidationError(null);
    onConfirmReschedule({
      targetSlotId: parsed.data.targetSlotId,
      rescheduleNote: parsed.data.rescheduleNote || undefined,
    });
  };

  return (
    <Modal open={Boolean(appointment)} onClose={onClose} maxWidth="max-w-2xl">
      <form onSubmit={handleSubmit} noValidate className="p-6">
        {/* Header (SCR-APT-10 Figma 17:1517) */}
        <div className="flex items-start gap-3 border-b border-border pb-4 pr-8">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <CalendarClock className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-lg font-extrabold text-ink">Đổi Lịch Hẹn Khám Ngoại Trú</h2>
            <p className="mt-0.5 text-xs text-sub">
              Soát xét và xác nhận dời lịch hẹn{' '}
              <span className="font-mono font-bold text-ink">#{appointment.appointmentCode}</span>{' '}
              (SCR-APT-10)
            </p>
          </div>
        </div>

        {/* Comparison Grid: Current vs Proposed Slot */}
        <div className="mt-5 grid grid-cols-1 items-center gap-3 sm:grid-cols-11">
          {/* Left: Current Schedule */}
          <div className="rounded-2xl border border-border bg-canvas/80 p-4 sm:col-span-5">
            <span className="inline-block rounded-md bg-slate-200/80 px-2.5 py-0.5 text-[11px] font-bold text-sub">
              Lịch hiện tại
            </span>
            <p className="mt-2.5 text-lg font-extrabold text-sub line-through">
              {appointment.scheduledTimeRange}
            </p>
            <p className="mt-0.5 text-xs font-medium text-sub">
              {appointment.scheduledDateFullLabel}
            </p>

            <div className="mt-3 space-y-1.5 border-t border-border pt-2.5 text-xs text-sub">
              <p className="flex items-center gap-1.5">
                <Building2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>
                  STT: <strong className="font-mono">{appointment.queueNumber ?? '012'}</strong> •{' '}
                  {appointment.roomCode.replace('P.', 'Buồng ')}
                </span>
              </p>
              <p className="flex items-center gap-1.5">
                <UserRound className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{appointment.doctorName}</span>
              </p>
            </div>
          </div>

          {/* Center Arrow */}
          <div className="flex justify-center sm:col-span-1">
            <div className="flex h-8 w-8 items-center justify-center rounded-full border border-sky-200 bg-sky-50 text-primary">
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </div>
          </div>

          {/* Right: Proposed New Slot */}
          <div className="rounded-2xl border-2 border-primary bg-sky-50/40 p-4 sm:col-span-5">
            <div className="flex items-center justify-between gap-2">
              <span className="inline-flex items-center gap-1 rounded-md bg-primary px-2.5 py-0.5 text-[11px] font-bold text-white">
                <span>Khung giờ mới đề xuất</span>
                <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
              </span>
              <span className="rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[10px] font-bold text-emerald-700">
                Trống chỗ
              </span>
            </div>

            {activeSlot && (
              <>
                <p className="mt-2.5 text-lg font-extrabold text-ink">
                  {activeSlot.scheduledTimeRange}
                </p>
                <p className="mt-0.5 text-xs font-bold text-ink">
                  {activeSlot.scheduledDateFullLabel}
                </p>

                <div className="mt-3 space-y-1.5 border-t border-sky-200/80 pt-2.5 text-xs text-primary">
                  <p className="flex items-center gap-1.5 font-semibold">
                    <Building2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>
                      STT dự kiến:{' '}
                      <strong className="font-mono">{activeSlot.estimatedQueueNumber}</strong> •{' '}
                      {activeSlot.roomCode.replace('P.', 'Buồng ')}
                    </span>
                  </p>
                  <p className="flex items-center gap-1.5 font-semibold">
                    <UserRound className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>{activeSlot.doctorName}</span>
                  </p>
                </div>
              </>
            )}
          </div>
        </div>

        {/* Selectable Proposed Slots */}
        <fieldset className="mt-4">
          <legend className="text-xs font-bold text-ink">
            Chọn khung giờ trống của {appointment.doctorName}:
          </legend>
          <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
            {proposedSlots.map((slot) => {
              const isSelected = slot.slotId === selectedSlotId;
              return (
                <button
                  key={slot.slotId}
                  type="button"
                  onClick={() => {
                    setSelectedSlotId(slot.slotId);
                    setValidationError(null);
                  }}
                  aria-pressed={isSelected}
                  className={`rounded-xl border p-2.5 text-left text-xs transition-colors ${
                    isSelected
                      ? 'border-primary bg-sky-50 font-bold text-primary'
                      : 'border-border bg-white text-ink hover:bg-canvas'
                  }`}
                >
                  <div className="font-extrabold">{slot.scheduledTimeRange}</div>
                  <div className="mt-0.5 text-[11px] text-sub">{slot.scheduledDateBadge}</div>
                  <div className="mt-1 font-mono text-[10px] font-semibold text-primary">
                    STT dự kiến: {slot.estimatedQueueNumber}
                  </div>
                </button>
              );
            })}
          </div>
        </fieldset>

        {/* Optional Note Input */}
        <div className="mt-4">
          <label htmlFor="reschedule-note-input" className="block text-xs font-semibold text-ink">
            Ghi chú đổi lịch (tùy chọn)
          </label>
          <input
            id="reschedule-note-input"
            type="text"
            value={rescheduleNote}
            onChange={(event) => {
              setRescheduleNote(event.target.value);
              if (validationError) setValidationError(null);
            }}
            placeholder="Ví dụ: Bận họp buổi sáng ngày 13/10, xin dời sang ca sáng Thứ Sáu..."
            className="mt-1.5 w-full rounded-xl border border-border bg-white px-3.5 py-2 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:outline-none"
          />
          {validationError && (
            <p role="alert" className="mt-1.5 text-xs font-semibold text-danger">
              {validationError}
            </p>
          )}
        </div>

        {/* Amber Warning Box (Figma 17:1517) */}
        <div className="mt-4 rounded-2xl border border-amber-200 bg-amber-50/70 p-4 text-xs text-amber-950">
          <p className="flex items-center gap-2 font-extrabold text-amber-900 uppercase">
            <AlertTriangle className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
            <span>LƯU Ý QUAN TRỌNG KHI ĐỔI LỊCH KHÁM NGOẠI TRÚ:</span>
          </p>
          <ol className="mt-2 list-decimal space-y-1.5 pl-5 leading-relaxed text-amber-900">
            <li>
              Khung giờ khám cũ (<strong>{appointment.scheduledTimeRange}</strong>{' '}
              {appointment.scheduledDateBadge}) sẽ lập tức được giải phóng trên hệ thống quản lý
              buồng khám để người bệnh khác có thể đăng ký.
            </li>
            <li>
              Số thứ tự giữ trước cũ (<strong>STT: {appointment.queueNumber ?? '012'}</strong>) sẽ
              bị thu hồi và hệ thống tự động cấp số thứ tự giữ trước mới theo lượt đăng ký ca mới (
              <strong>STT dự kiến: {activeSlot?.estimatedQueueNumber ?? '005'}</strong>).
            </li>
            <li>
              <strong>Chính sách miễn phí:</strong> MedFlow hỗ trợ đổi lịch khám ngoại trú tự nguyện
              hoàn toàn miễn phí khi thực hiện trước giờ khám tối thiểu 02 tiếng.
            </li>
          </ol>
        </div>

        {/* Fee Summary Bar */}
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-canvas px-4 py-3 text-xs">
          <div>
            <p className="font-bold text-ink">
              Phí dịch vụ chuyên khoa: {appointment.listedFee.toLocaleString('vi-VN')} VNĐ
            </p>
            <p className="text-[11px] text-sub">
              (Không thay đổi • Dịch vụ tự nguyện 100% không áp dụng BHYT)
            </p>
          </div>
          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[11px] font-bold text-emerald-700">
            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Không phát sinh phí đổi lịch (0 VNĐ)</span>
          </span>
        </div>

        {/* Modal Footer */}
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-border bg-white px-4 py-2.5 text-xs font-bold text-ink transition-colors hover:bg-canvas"
          >
            Giữ nguyên lịch hiện tại / Đóng
          </button>

          <button
            type="submit"
            disabled={isSubmitting}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark disabled:opacity-60"
          >
            <span>{isSubmitting ? 'Đang cập nhật...' : 'Xác Nhận Đổi Sang Lịch Mới'}</span>
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        </div>
      </form>
    </Modal>
  );
}
