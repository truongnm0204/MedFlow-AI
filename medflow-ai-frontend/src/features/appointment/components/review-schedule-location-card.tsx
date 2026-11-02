import { Award, Building2, CalendarClock, Clock, Info, MapPin, Stethoscope } from 'lucide-react';

import type { ReviewScheduleLocationInfo } from '../types/review-booking.types';

interface ReviewScheduleLocationCardProps {
  scheduleLocation: ReviewScheduleLocationInfo;
}

export function ReviewScheduleLocationCard({ scheduleLocation }: ReviewScheduleLocationCardProps) {
  return (
    <section
      aria-label="2. Thời gian khám và địa điểm tiếp nhận"
      className="rounded-2xl border border-border bg-white p-6 shadow-xs sm:p-7"
    >
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <div className="flex items-center gap-2.5">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-sky-100 bg-sky-50 text-primary">
            <CalendarClock className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-base font-bold tracking-tight text-ink">
              2. THỜI GIAN KHÁM & ĐỊA ĐIỂM TIẾP NHẬN
            </h2>
            <p className="text-xs text-sub">
              Phân luồng tự động buồng khám chuyên khoa kỹ thuật cao
            </p>
          </div>
        </div>

        <span className="inline-flex items-center gap-1.5 rounded-md border border-sky-200/80 bg-sky-50 px-3 py-1 text-xs font-medium text-primary-dark">
          <Clock className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{scheduleLocation.durationBadgeLabel}</span>
        </span>
      </div>

      {/* Doctor Summary Banner */}
      <div className="mt-5 rounded-xl border border-sky-100 bg-gradient-to-r from-sky-50/60 to-canvas p-4">
        <div className="flex items-center gap-3.5">
          <div className="relative h-14 w-14 shrink-0 overflow-hidden rounded-xl border-2 border-white bg-sky-100 shadow-xs">
            {scheduleLocation.doctorAvatarUrl ? (
              <img
                src={scheduleLocation.doctorAvatarUrl}
                alt={scheduleLocation.doctorName}
                className="h-full w-full object-cover"
              />
            ) : (
              <div className="flex h-full w-full items-center justify-center text-primary">
                <Stethoscope className="h-6 w-6" aria-hidden="true" />
              </div>
            )}
          </div>

          <div className="min-w-0 flex-1 space-y-1">
            <span className="inline-flex items-center gap-1.5 rounded bg-primary px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-white">
              <Award className="h-3 w-3" aria-hidden="true" />
              <span>{scheduleLocation.doctorRoleBadge}</span>
            </span>

            <h3 className="text-base font-bold text-ink">{scheduleLocation.doctorName}</h3>

            <p className="text-xs text-sub">{scheduleLocation.doctorCredentialsSubtitle}</p>
          </div>
        </div>
      </div>

      {/* Schedule & Room Grid */}
      <div className="mt-5 grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            CHUYÊN KHOA THĂM KHÁM
          </p>
          <p className="mt-1 inline-flex items-center gap-1.5 text-sm font-semibold text-ink">
            <MapPin className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>{scheduleLocation.specialtyName}</span>
          </p>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            DỊCH VỤ KỸ THUẬT Y TẾ
          </p>
          <p className="mt-1 text-sm font-semibold text-ink">
            {scheduleLocation.serviceName}{' '}
            <span className="font-mono text-xs font-bold text-ink">
              (Mã {scheduleLocation.serviceCode})
            </span>
          </p>
        </div>

        {/* Highlighted Time Slot Box */}
        <div className="rounded-xl border border-sky-200 bg-sky-50/70 p-3.5">
          <p className="text-xs font-bold uppercase tracking-wider text-sky-800">
            KHUNG GIỜ HẸN TRƯỚC
          </p>
          <div className="mt-1.5 flex items-start gap-2">
            <Clock className="mt-0.5 h-4 w-4 shrink-0 text-primary-dark" aria-hidden="true" />
            <p className="text-base font-bold leading-snug text-sky-950">
              {scheduleLocation.scheduledSlotDisplay}
            </p>
          </div>
        </div>

        {/* Allocated Room Box */}
        <div className="rounded-xl border border-border bg-canvas p-3.5">
          <p className="text-xs font-medium uppercase tracking-wider text-sub">
            BUỒNG KHÁM PHÂN BỔ
          </p>
          <div className="mt-1.5 flex items-start gap-2">
            <Building2 className="mt-0.5 h-4 w-4 shrink-0 text-sub" aria-hidden="true" />
            <p className="text-sm font-bold leading-snug text-ink">
              {scheduleLocation.allocatedRoomTitle}
            </p>
          </div>
        </div>

        {/* Duration Footnote */}
        <div className="flex items-center gap-1.5 pt-1 text-xs text-sub sm:col-span-2">
          <Info className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
          <span>{scheduleLocation.durationFootnote}</span>
        </div>
      </div>
    </section>
  );
}
