import { ArrowRight, CalendarCheck2 } from 'lucide-react';

import type {
  BookingDateOption,
  BookingPatientProfile,
  BookingServiceOnlyInfo,
  ServiceShiftOption,
} from '../types/slot-booking.types';

interface BookingServiceStickyFooterProps {
  serviceInfo: BookingServiceOnlyInfo;
  selectedProfile?: BookingPatientProfile;
  selectedDate?: BookingDateOption;
  selectedShift?: ServiceShiftOption;
  onContinue: () => void;
}

export function BookingServiceStickyFooter({
  serviceInfo,
  selectedProfile,
  selectedDate,
  selectedShift,
  onContinue,
}: BookingServiceStickyFooterProps) {
  const formattedPrice = new Intl.NumberFormat('vi-VN').format(serviceInfo.price);
  const canContinue = Boolean(selectedProfile && selectedDate && selectedShift);

  return (
    <section
      aria-label="Tóm tắt lịch khám dịch vụ đã chọn"
      className="mt-6 rounded-2xl border border-sky-200 bg-white p-4 shadow-md sm:px-6 sm:py-4"
    >
      <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-center">
        <div className="flex items-start gap-3.5">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <CalendarCheck2 className="h-5 w-5" aria-hidden="true" />
          </div>

          <div className="space-y-0.5 text-xs">
            <p className="font-bold text-ink sm:text-sm">
              <span className="text-primary">ĐÃ CHỌN:</span>{' '}
              {selectedShift && selectedDate ? (
                <>
                  {selectedShift.title} ({selectedShift.timeRange}) • {selectedDate.fullDateDisplay}
                </>
              ) : (
                'Vui lòng chọn ngày và ca khám'
              )}
            </p>
            <p className="text-sub">
              Người khám:{' '}
              <strong className="font-semibold text-ink">
                {selectedProfile?.fullName ?? 'Chưa chọn'}
              </strong>{' '}
              <span>{selectedProfile?.relationshipLabel ?? ''}</span>
            </p>
            <p className="text-sub">
              Dịch vụ: <strong className="font-semibold text-ink">{serviceInfo.serviceName}</strong>{' '}
              <span className="font-mono text-xs text-ink">({serviceInfo.serviceCode})</span> •{' '}
              {serviceInfo.departmentName}
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border pt-3 lg:border-t-0 lg:pt-0">
          <div className="text-left lg:text-right">
            <p className="text-[11px] text-sub">Khám dịch vụ ngoại trú • giá niêm yết</p>
            <p className="text-lg font-extrabold text-primary sm:text-xl">
              {formattedPrice} <span className="text-xs font-bold">VNĐ</span>
            </p>
          </div>

          <button
            type="button"
            disabled={!canContinue}
            onClick={onContinue}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-3 text-xs font-bold text-white shadow-sm transition-colors hover:bg-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-60 sm:text-sm"
          >
            <span>Tiếp tục: Soát xét Đặt lịch</span>
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </div>
    </section>
  );
}
