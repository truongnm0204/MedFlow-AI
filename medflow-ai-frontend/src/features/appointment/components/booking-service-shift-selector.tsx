import { Check, Clock, Moon, Sun } from 'lucide-react';

import type { ServiceShiftOption } from '../types/slot-booking.types';

interface BookingServiceShiftSelectorProps {
  shifts: ServiceShiftOption[];
  selectedShiftId: string;
  onSelectShift: (shiftId: string) => void;
}

export function BookingServiceShiftSelector({
  shifts,
  selectedShiftId,
  onSelectShift,
}: BookingServiceShiftSelectorProps) {
  return (
    <section
      aria-labelledby="section-service-shift-heading"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6 space-y-4"
    >
      <div className="flex items-start gap-3">
        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
          <Clock className="h-4 w-4" aria-hidden="true" />
        </div>
        <div>
          <h2
            id="section-service-shift-heading"
            className="text-sm font-bold text-ink sm:text-base"
          >
            3. Chọn Ca Khám Dịch Vụ
          </h2>
          <p className="text-xs text-sub">
            Chọn ca khám buổi sáng hoặc buổi chiều; quầy tiếp đón sẽ phân bổ phòng khám theo thứ tự
            ưu tiên.
          </p>
        </div>
      </div>

      <div
        role="radiogroup"
        aria-label="Chọn ca khám dịch vụ"
        className="grid grid-cols-1 gap-4 md:grid-cols-2"
      >
        {shifts.map((item) => {
          const isFull = item.status === 'FULL';
          const isSelected = item.id === selectedShiftId && !isFull;
          const isMorning = item.shift === 'MORNING';

          return (
            <button
              key={item.id}
              type="button"
              role="radio"
              aria-checked={isSelected}
              disabled={isFull}
              onClick={() => onSelectShift(item.id)}
              className={`flex flex-col justify-between rounded-2xl border p-4 text-left transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:p-5 ${
                isSelected
                  ? 'border-2 border-primary bg-sky-50/40 shadow-xs'
                  : isFull
                    ? 'cursor-not-allowed border-border/70 bg-canvas/80 text-sub opacity-60'
                    : 'border-border bg-white hover:border-sky-300 hover:bg-canvas/50'
              }`}
            >
              <div>
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-center gap-2.5">
                    <div
                      className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${
                        isSelected
                          ? 'bg-primary text-white'
                          : isMorning
                            ? 'bg-amber-50 text-amber-600'
                            : 'bg-sky-50 text-primary'
                      }`}
                    >
                      {isMorning ? (
                        <Sun className="h-5 w-5" aria-hidden="true" />
                      ) : (
                        <Moon className="h-5 w-5" aria-hidden="true" />
                      )}
                    </div>

                    <div>
                      <div className="flex items-center gap-1.5">
                        <h3 className="text-sm font-bold text-ink sm:text-base">{item.title}</h3>
                        {isSelected && (
                          <Check className="h-4 w-4 text-primary" aria-hidden="true" />
                        )}
                      </div>
                      <p className="text-xs font-semibold text-primary">
                        Khung giờ ca: {item.timeRange}
                      </p>
                    </div>
                  </div>

                  <span
                    className={`shrink-0 rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${
                      isSelected
                        ? 'border-primary bg-primary text-white'
                        : isFull
                          ? 'border-slate-200 bg-slate-200/70 text-sub'
                          : item.status === 'FEW_SLOTS'
                            ? 'border-amber-200 bg-amber-50 text-amber-800'
                            : 'border-emerald-200 bg-emerald-50 text-emerald-700'
                    }`}
                  >
                    {isSelected ? 'Đang chọn' : item.badgeText}
                  </span>
                </div>

                <p className="mt-3 text-xs leading-relaxed text-sub">{item.description}</p>
              </div>

              <div className="mt-3.5 border-t border-border/80 pt-2.5 text-[11px] font-medium text-ink">
                {item.checkInWindow}
              </div>
            </button>
          );
        })}
      </div>
    </section>
  );
}
