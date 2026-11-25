import { useMemo, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react';

import type { BookingDateOption } from '../types/slot-booking.types';

const DATES_PER_PAGE = 7;

interface BookingDateSelectorProps {
  monthLabel: string;
  dates: BookingDateOption[];
  selectedDateIso: string;
  onSelectDate: (dateIso: string) => void;
}

function formatMonthLabelFromIso(dateIso: string | undefined, fallback: string): string {
  if (!dateIso) return fallback;
  const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(dateIso);
  if (!match) return fallback;
  const year = Number(match[1]);
  const month = Number(match[2]);
  if (!Number.isFinite(year) || !Number.isFinite(month)) return fallback;
  return `Tháng ${month}, ${year}`;
}

export function BookingDateSelector({
  monthLabel,
  dates,
  selectedDateIso,
  onSelectDate,
}: BookingDateSelectorProps) {
  const totalPages = Math.max(1, Math.ceil(dates.length / DATES_PER_PAGE));

  const selectedPageIndex = useMemo(() => {
    const idx = dates.findIndex((item) => item.dateIso === selectedDateIso);
    return idx >= 0 ? Math.floor(idx / DATES_PER_PAGE) : 0;
  }, [dates, selectedDateIso]);

  const [manualPageState, setManualPageState] = useState<{
    anchorSelectedIso: string;
    page: number;
  } | null>(null);

  const rawPage =
    manualPageState && manualPageState.anchorSelectedIso === selectedDateIso
      ? manualPageState.page
      : selectedPageIndex;
  const currentPage = Math.min(Math.max(0, rawPage), totalPages - 1);

  const visibleDates = useMemo(() => {
    if (dates.length <= DATES_PER_PAGE) {
      return dates;
    }
    const start = currentPage * DATES_PER_PAGE;
    return dates.slice(start, start + DATES_PER_PAGE);
  }, [currentPage, dates]);

  const displayedMonthLabel =
    currentPage === 0 ? monthLabel : formatMonthLabelFromIso(visibleDates[0]?.dateIso, monthLabel);

  const canGoPrev = currentPage > 0;
  const canGoNext = currentPage < totalPages - 1;

  return (
    <section
      aria-labelledby="section-date-heading"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="mb-4 flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
        <div className="flex items-start gap-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
            <CalendarDays className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 id="section-date-heading" className="text-sm font-bold text-ink sm:text-base">
              2. Chọn Ngày Khám
            </h2>
            <p className="text-xs text-sub">
              Lịch làm việc thực tế được đồng bộ trực tiếp từ phòng khám Bác sĩ
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 self-end sm:self-auto">
          <span className="text-xs font-bold text-ink">{displayedMonthLabel}</span>
          <div className="flex items-center gap-1">
            <button
              type="button"
              aria-label="Tuần trước"
              disabled={!canGoPrev}
              onClick={() =>
                setManualPageState({
                  anchorSelectedIso: selectedDateIso,
                  page: Math.max(0, currentPage - 1),
                })
              }
              className="flex h-7 w-7 items-center justify-center rounded-lg border border-border bg-white text-sub transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
            >
              <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
            <button
              type="button"
              aria-label="Tuần tiếp theo"
              disabled={!canGoNext}
              onClick={() =>
                setManualPageState({
                  anchorSelectedIso: selectedDateIso,
                  page: Math.min(totalPages - 1, currentPage + 1),
                })
              }
              className="flex h-7 w-7 items-center justify-center rounded-lg border border-border bg-white text-sub transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
            >
              <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          </div>
        </div>
      </div>

      <div
        role="listbox"
        aria-label="Danh sách ngày khám trong tuần"
        className="grid grid-cols-2 gap-2.5 sm:grid-cols-4 lg:grid-cols-7"
      >
        {visibleDates.map((item) => {
          const isDisabled = item.status === 'FULL' || item.status === 'OFF';
          const isSelected = item.dateIso === selectedDateIso && !isDisabled;

          let badgeClass = 'bg-emerald-50 text-emerald-700 border-emerald-200';
          if (isSelected) {
            badgeClass = 'bg-white text-primary border-white font-bold';
          } else if (item.status === 'FEW_SLOTS') {
            badgeClass = 'bg-amber-50 text-amber-700 border-amber-200';
          } else if (isDisabled) {
            badgeClass = 'bg-slate-200/70 text-sub border-slate-200';
          }

          return (
            <button
              key={item.dateIso}
              type="button"
              role="option"
              aria-selected={isSelected}
              disabled={isDisabled}
              onClick={() => {
                setManualPageState(null);
                onSelectDate(item.dateIso);
              }}
              className={`flex flex-col items-center justify-between rounded-xl border p-3 text-center transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                isSelected
                  ? 'border-primary bg-primary text-white shadow-sm'
                  : isDisabled
                    ? 'cursor-not-allowed border-border/70 bg-canvas/80 text-sub opacity-60'
                    : 'border-border bg-white text-ink hover:border-sky-300 hover:bg-sky-50/20'
              }`}
            >
              <span
                className={`flex items-center gap-1 text-[11px] font-semibold ${
                  isSelected ? 'text-sky-100' : isDisabled ? 'text-sub' : 'text-ink'
                }`}
              >
                {item.isToday && (
                  <span
                    aria-hidden="true"
                    className={`h-1.5 w-1.5 rounded-full ${isSelected ? 'bg-white' : 'bg-primary'}`}
                  />
                )}
                <span>{item.dayTopLabel}</span>
              </span>

              <span
                className={`my-1 text-xl font-extrabold ${
                  item.status === 'FULL' ? 'line-through' : ''
                }`}
              >
                {item.dayNumber}
              </span>

              <span className={`mb-2 text-[11px] ${isSelected ? 'text-sky-100' : 'text-sub'}`}>
                {item.subDateLabel}
              </span>

              <span
                className={`w-full rounded-md border px-1.5 py-0.5 text-[10px] font-semibold ${badgeClass}`}
              >
                {item.badgeText}
              </span>
            </button>
          );
        })}
      </div>
    </section>
  );
}
