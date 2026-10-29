import { Check, Clock, Info, Moon, Sun } from 'lucide-react';

import type { BookingTimeSlot, DoctorDayShiftSchedule } from '../types/slot-booking.types';

interface BookingSlotGridProps {
  morningSlots: BookingTimeSlot[];
  afternoonSlots: BookingTimeSlot[];
  activeDayShift?: DoctorDayShiftSchedule;
  selectedSlotId: string;
  onSelectSlot: (slotId: string) => void;
}

export function BookingSlotGrid({
  morningSlots,
  afternoonSlots,
  activeDayShift,
  selectedSlotId,
  onSelectSlot,
}: BookingSlotGridProps) {
  const availableMorningCount = morningSlots.filter((s) => s.status !== 'FULL').length;
  const availableAfternoonCount = afternoonSlots.filter((s) => s.status !== 'FULL').length;

  return (
    <section
      aria-labelledby="section-slot-heading"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6 space-y-5"
    >
      <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
        <div className="flex items-start gap-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
            <Clock className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 id="section-slot-heading" className="text-sm font-bold text-ink sm:text-base">
              3. Chọn Khung Giờ Thăm Khám
            </h2>
            <p className="text-xs text-sub">
              Thời lượng theo khung giờ và cấu hình điểm phục vụ của cơ sở.
            </p>
          </div>
        </div>

        {activeDayShift?.shiftSummaryLabel && (
          <span className="inline-flex items-center gap-1.5 rounded-lg border border-sky-200 bg-sky-50/70 px-3 py-1 text-[11px] font-semibold text-primary">
            <span>{activeDayShift.shiftSummaryLabel}</span>
          </span>
        )}
      </div>

      {activeDayShift?.offShiftNote && (
        <div className="flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50/70 px-3.5 py-2.5 text-xs text-amber-900">
          <Info className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <span>{activeDayShift.offShiftNote}</span>
        </div>
      )}

      {/* Morning Shift */}
      {morningSlots.length > 0 && (
        <div className="space-y-3">
          <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-ink">
            <Sun className="h-4 w-4 text-amber-500" aria-hidden="true" />
            <span>CA KHÁM BUỔI SÁNG (07:30 - 11:30)</span>
            <span className="font-normal normal-case text-sub">
              • Còn {availableMorningCount} khung giờ khả dụng
            </span>
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {morningSlots.map((slot) => {
              const isFull = slot.status === 'FULL';
              const isSelected = slot.id === selectedSlotId && !isFull;

              return (
                <button
                  key={slot.id}
                  type="button"
                  disabled={isFull}
                  aria-pressed={isSelected}
                  onClick={() => onSelectSlot(slot.id)}
                  className={`flex items-center justify-between rounded-xl border px-3.5 py-3 text-left transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                    isSelected
                      ? 'border-primary bg-primary text-white shadow-sm'
                      : isFull
                        ? 'cursor-not-allowed border-border/70 bg-canvas/80 text-sub opacity-60'
                        : 'border-border bg-white text-ink hover:border-sky-300 hover:bg-sky-50/20'
                  }`}
                >
                  <span
                    className={`flex items-center gap-1.5 text-xs font-bold ${
                      isFull ? 'line-through text-sub' : ''
                    }`}
                  >
                    {isSelected && <Check className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />}
                    <span>{slot.timeRange}</span>
                  </span>

                  <span
                    className={`rounded-md px-2 py-0.5 text-[10px] font-semibold ${
                      isSelected
                        ? 'bg-white text-primary font-bold'
                        : isFull
                          ? 'bg-slate-200/80 text-sub'
                          : 'bg-emerald-50 text-emerald-700'
                    }`}
                  >
                    {isSelected ? 'Đang chọn' : slot.badgeText}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* Afternoon Shift */}
      {afternoonSlots.length > 0 && (
        <div className="space-y-3 pt-1">
          <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-ink">
            <Moon className="h-4 w-4 text-primary" aria-hidden="true" />
            <span>CA KHÁM BUỔI CHIỀU (13:30 - 16:30)</span>
            <span className="font-normal normal-case text-sub">
              • Còn {availableAfternoonCount} khung giờ khả dụng
            </span>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            {afternoonSlots.map((slot) => {
              const isFull = slot.status === 'FULL';
              const isLastSlot = slot.status === 'LAST_SLOT';
              const isSelected = slot.id === selectedSlotId && !isFull;

              return (
                <button
                  key={slot.id}
                  type="button"
                  disabled={isFull}
                  aria-pressed={isSelected}
                  onClick={() => onSelectSlot(slot.id)}
                  className={`flex flex-col items-center justify-center gap-1.5 rounded-xl border px-3 py-3 text-center transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                    isSelected
                      ? 'border-primary bg-primary text-white shadow-sm'
                      : isLastSlot
                        ? 'border-amber-300 bg-amber-50/20 text-ink hover:border-amber-400'
                        : 'border-border bg-white text-ink hover:border-sky-300 hover:bg-sky-50/20'
                  }`}
                >
                  <span className="text-xs font-bold">{slot.timeRange}</span>

                  <span
                    className={`rounded-md px-2 py-0.5 text-[10px] font-semibold ${
                      isSelected
                        ? 'bg-white text-primary font-bold'
                        : isLastSlot
                          ? 'bg-amber-100 text-amber-800'
                          : 'bg-emerald-50 text-emerald-700'
                    }`}
                  >
                    {isSelected ? 'Đang chọn' : slot.badgeText}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      )}
    </section>
  );
}
