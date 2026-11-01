import { Calendar, Clock, DoorOpen, Info, ShieldPlus, Timer } from 'lucide-react';

import type { BookingReviewSummary } from '../types/booking-review.types';

interface ReviewScheduleSectionProps {
  summary: BookingReviewSummary;
}

export function ReviewScheduleSection({ summary }: ReviewScheduleSectionProps) {
  return (
    <section
      aria-label="Thời gian khám và địa điểm tiếp nhận"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <Calendar className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-sm font-bold text-ink uppercase">
              2. THỜI GIAN KHÁM &amp; ĐỊA ĐIỂM TIẾP NHẬN
            </h2>
            <p className="text-xs text-sub">
              Phân luồng tự động buồng khám chuyên khoa kỹ thuật cao
            </p>
          </div>
        </div>

        {summary.durationMinutes && (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-bold text-primary">
            <Timer className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{summary.durationMinutes} phút/lượt khám</span>
          </span>
        )}
      </div>

      {summary.mode === 'doctor' && summary.doctorName && (
        <div className="mt-5 flex items-center gap-4 rounded-xl border border-sky-100 bg-sky-50/50 p-4">
          <img
            src={summary.doctorAvatarUrl ?? ''}
            alt={summary.doctorName}
            className="h-14 w-14 shrink-0 rounded-xl object-cover"
          />
          <div className="min-w-0 flex-1">
            {summary.doctorRoleTag && (
              <span className="inline-block rounded-md bg-primary px-2 py-0.5 text-[10px] font-bold text-white uppercase">
                {summary.doctorRoleTag}
              </span>
            )}
            <p className="mt-1 text-base font-bold text-ink">{summary.doctorName}</p>
            <p className="text-xs text-sub">{summary.specialtyName}</p>
          </div>
        </div>
      )}

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
            CHUYÊN KHOA THĂM KHÁM
          </p>
          <p className="mt-1 flex items-center gap-1.5 text-sm font-semibold text-ink">
            <ShieldPlus className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <span>{summary.specialtyName}</span>
          </p>
        </div>

        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
            DỊCH VỤ KỸ THUẬT Y TẾ
          </p>
          <p className="mt-1 text-sm font-semibold text-ink">
            {summary.serviceName} (Mã <span className="font-mono">{summary.serviceCode}</span>)
          </p>
        </div>
      </div>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <div className="rounded-xl border border-sky-200 bg-sky-50 p-4">
          <p className="flex items-center gap-1.5 text-[11px] font-bold tracking-wide text-sub uppercase">
            <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>KHUNG GIỜ HẸN TRƯỚC</span>
          </p>
          <p className="mt-1.5 text-base font-bold text-primary">{summary.scheduleLabel}</p>
        </div>

        <div className="rounded-xl border border-border bg-canvas p-4">
          <p className="flex items-center gap-1.5 text-[11px] font-bold tracking-wide text-sub uppercase">
            <DoorOpen className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>BUỒNG KHÁM PHÂN BỔ</span>
          </p>
          <p className="mt-1.5 text-base font-bold text-ink">{summary.roomLabel}</p>
        </div>
      </div>

      {summary.durationDescription && (
        <p className="mt-4 flex items-center gap-1.5 text-xs text-sub">
          <Info className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <span>{summary.durationDescription}</span>
        </p>
      )}
    </section>
  );
}
