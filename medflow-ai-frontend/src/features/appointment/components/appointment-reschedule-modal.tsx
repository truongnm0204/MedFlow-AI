import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  Building2,
  CalendarClock,
  CalendarX2,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Loader2,
  RotateCcw,
  UserRound,
} from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Modal } from '@/shared/ui/modal';

import { fetchRescheduleScheduleApi } from '../api/appointment-detail.api';
import { rescheduleAppointmentSchema } from '../schemas/appointment-detail.schema';
import type { AppointmentListItem } from '../types/appointment-list.types';

const DATES_PER_STRIP = 7;

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
  const [requestedDateIso, setRequestedDateIso] = useState<string>('');
  const [datePage, setDatePage] = useState<number>(0);
  const [selectedSlotId, setSelectedSlotId] = useState<string>('');
  const [rescheduleNote, setRescheduleNote] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const scheduleQuery = useQuery({
    queryKey: [
      'appointment-reschedule-slots',
      appointment?.id,
      appointment?.doctorId,
      appointment?.serviceId,
      requestedDateIso || 'initial',
    ],
    queryFn: () => fetchRescheduleScheduleApi(appointment!, requestedDateIso || undefined),
    enabled: Boolean(appointment),
    retry: false,
  });

  const dates = useMemo(() => scheduleQuery.data?.dates ?? [], [scheduleQuery.data?.dates]);
  const totalDatePages = Math.max(1, Math.ceil(dates.length / DATES_PER_STRIP));
  const clampedDatePage = Math.min(Math.max(0, datePage), totalDatePages - 1);
  const visibleDates = useMemo(
    () => dates.slice(clampedDatePage * DATES_PER_STRIP, (clampedDatePage + 1) * DATES_PER_STRIP),
    [clampedDatePage, dates],
  );

  if (!appointment) return null;

  const isDoctorMode = Boolean(appointment.doctorId?.trim());
  const scheduleOwnerLabel = isDoctorMode
    ? appointment.doctorName
    : appointment.serviceName || 'dịch vụ khám';
  const currentDoctorLabel = appointment.doctorName?.trim() || 'Bác sĩ điều phối theo ca khám';
  const currentRoomLabel = appointment.roomCode?.trim()
    ? appointment.roomCode.replace('P.', 'Buồng ')
    : 'Theo điều phối';

  const proposedSlots = scheduleQuery.data?.slots ?? [];
  const activeDateIso =
    scheduleQuery.data?.activeDateIso ?? requestedDateIso ?? appointment.scheduledDate;
  const isLoadingSlots = scheduleQuery.isLoading;
  const slotsError = scheduleQuery.isError
    ? getErrorMessage(scheduleQuery.error, 'Không thể tải danh sách khung giờ trống để đổi lịch.')
    : null;

  const activeSlot =
    proposedSlots.find((s) => s.slotId === selectedSlotId) ?? proposedSlots[0] ?? null;

  const canConfirmReschedule =
    !isSubmitting && !isLoadingSlots && !slotsError && Boolean(activeSlot);

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (!activeSlot) {
      setValidationError('Hiện chưa có khung giờ trống hợp lệ để xác nhận đổi lịch.');
      return;
    }

    const parsed = rescheduleAppointmentSchema.safeParse({
      targetSlotId: activeSlot.slotId,
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
                  STT: <strong className="font-mono">{appointment.queueNumber ?? '---'}</strong> •{' '}
                  {currentRoomLabel}
                </span>
              </p>
              <p className="flex items-center gap-1.5">
                <UserRound className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{currentDoctorLabel}</span>
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
              {activeSlot && (
                <span className="rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[10px] font-bold text-emerald-700">
                  Trống chỗ
                </span>
              )}
            </div>

            {isLoadingSlots ? (
              <div
                role="status"
                className="mt-4 flex items-center gap-2 py-4 text-xs font-medium text-sub"
              >
                <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
                <span>Đang tải khung giờ trống...</span>
              </div>
            ) : activeSlot ? (
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
                      {activeSlot.estimatedQueueNumber ? (
                        <>
                          STT dự kiến:{' '}
                          <strong className="font-mono">{activeSlot.estimatedQueueNumber}</strong>
                        </>
                      ) : (
                        <span>{activeSlot.remainingLabel ?? 'Cấp STT mới khi xác nhận'}</span>
                      )}{' '}
                      • {activeSlot.roomCode.replace('P.', 'Buồng ')}
                    </span>
                  </p>
                  <p className="flex items-center gap-1.5 font-semibold">
                    <UserRound className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>{activeSlot.doctorName}</span>
                  </p>
                </div>
              </>
            ) : (
              <p className="mt-3 py-3 text-xs text-sub">
                Chưa có khung giờ trống khả dụng để chọn dời lịch.
              </p>
            )}
          </div>
        </div>

        {/* Multi-date selector across BookingWindowDays */}
        {dates.length > 0 && (
          <div className="mt-4 rounded-xl border border-border bg-canvas/60 p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-bold text-ink">Chọn ngày khám mới:</span>
              {totalDatePages > 1 && (
                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    aria-label="Tuần trước"
                    disabled={clampedDatePage === 0}
                    onClick={() => setDatePage((prev) => Math.max(0, prev - 1))}
                    className="flex h-6 w-6 items-center justify-center rounded-md border border-border bg-white text-sub transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    aria-label="Tuần tiếp theo"
                    disabled={clampedDatePage >= totalDatePages - 1}
                    onClick={() => setDatePage((prev) => Math.min(totalDatePages - 1, prev + 1))}
                    className="flex h-6 w-6 items-center justify-center rounded-md border border-border bg-white text-sub transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                </div>
              )}
            </div>

            <div
              role="listbox"
              aria-label="Chọn ngày đổi lịch khám"
              className="mt-2 grid grid-cols-2 gap-1.5 sm:grid-cols-4 lg:grid-cols-7"
            >
              {visibleDates.map((d) => {
                const isDateDisabled = d.status === 'OFF' || d.status === 'FULL';
                const isDateSelected = d.dateIso === activeDateIso && !isDateDisabled;
                return (
                  <button
                    key={d.dateIso}
                    type="button"
                    role="option"
                    aria-selected={isDateSelected}
                    disabled={isDateDisabled}
                    onClick={() => {
                      setRequestedDateIso(d.dateIso);
                      setSelectedSlotId('');
                      setValidationError(null);
                    }}
                    className={`rounded-lg border px-2 py-1.5 text-center text-[11px] transition-colors ${
                      isDateSelected
                        ? 'border-primary bg-primary font-bold text-white'
                        : isDateDisabled
                          ? 'cursor-not-allowed border-border/70 bg-white/60 text-sub opacity-60'
                          : 'border-border bg-white text-ink hover:border-sky-300'
                    }`}
                  >
                    <div className="truncate font-semibold">{d.dayTopLabel}</div>
                    <div className="font-extrabold">{d.subDateLabel}</div>
                  </button>
                );
              })}
            </div>
          </div>
        )}

        {/* Selectable Proposed Slots / Loading / Error / Empty */}
        <div className="mt-4">
          {isLoadingSlots ? (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3" aria-busy="true">
              <div className="h-16 animate-pulse rounded-xl border border-border bg-canvas" />
              <div className="h-16 animate-pulse rounded-xl border border-border bg-canvas" />
              <div className="h-16 animate-pulse rounded-xl border border-border bg-canvas" />
            </div>
          ) : slotsError ? (
            <div
              role="alert"
              className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-900"
            >
              <div className="flex items-center gap-2">
                <AlertCircle className="h-4 w-4 shrink-0 text-danger" aria-hidden="true" />
                <span>{slotsError}</span>
              </div>
              <button
                type="button"
                onClick={() => void scheduleQuery.refetch()}
                className="inline-flex items-center gap-1 rounded-lg border border-rose-200 bg-white px-2.5 py-1 font-bold text-rose-800 hover:bg-rose-50"
              >
                <RotateCcw className="h-3 w-3" aria-hidden="true" />
                <span>Thử lại</span>
              </button>
            </div>
          ) : proposedSlots.length === 0 ? (
            <div
              role="status"
              className="flex items-center gap-2.5 rounded-xl border border-border bg-canvas px-4 py-3 text-xs text-sub"
            >
              <CalendarX2 className="h-4 w-4 shrink-0 text-sub" aria-hidden="true" />
              <span>
                Hiện chưa có khung giờ trống phù hợp của {scheduleOwnerLabel} để đổi lịch.
              </span>
            </div>
          ) : (
            <fieldset>
              <legend className="text-xs font-bold text-ink">
                Chọn khung giờ trống của {scheduleOwnerLabel}:
              </legend>
              <div className="mt-2 grid max-h-56 grid-cols-1 gap-2 overflow-y-auto pr-1 sm:grid-cols-3">
                {proposedSlots.map((slot) => {
                  const isSelected = slot.slotId === activeSlot?.slotId;
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
                        {slot.estimatedQueueNumber
                          ? `STT dự kiến: ${slot.estimatedQueueNumber}`
                          : (slot.remainingLabel ?? 'Cấp STT mới khi xác nhận')}
                      </div>
                    </button>
                  );
                })}
              </div>
            </fieldset>
          )}
        </div>

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
            placeholder="Ví dụ: Bận công tác đột xuất buổi sáng, xin dời sang ca khám khác..."
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
              Số thứ tự giữ trước cũ (<strong>STT: {appointment.queueNumber ?? '---'}</strong>) sẽ
              bị thu hồi và hệ thống tự động cấp số thứ tự giữ trước mới sau khi xác nhận đổi sang
              ca mới.
            </li>
            <li>
              <strong>Chính sách miễn phí:</strong> MedFlow hỗ trợ đổi lịch khám ngoại trú tự nguyện
              hoàn toàn miễn phí khi thực hiện đúng thời hạn quy định trước giờ khám.
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
            disabled={!canConfirmReschedule}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark disabled:cursor-not-allowed disabled:opacity-60"
          >
            <span>{isSubmitting ? 'Đang cập nhật...' : 'Xác Nhận Đổi Sang Lịch Mới'}</span>
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        </div>
      </form>
    </Modal>
  );
}
