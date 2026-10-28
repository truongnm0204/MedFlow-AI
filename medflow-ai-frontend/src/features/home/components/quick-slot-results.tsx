import { getErrorMessage } from '@/shared/lib/get-error-message';

import type { QuickSlotItem } from '../types/home.types';

interface QuickSlotResultsProps {
  validationError: string | null;
  isError: boolean;
  error: unknown;
  isSuccess: boolean;
  slots: QuickSlotItem[] | undefined;
  selectedSlot: QuickSlotItem | null;
  onSelectSlot: (slot: QuickSlotItem) => void;
}

/** Hiển thị lỗi kiểm tra hợp lệ hoặc danh sách khung giờ trống sau khi tìm kiếm nhanh. */
export function QuickSlotResults({
  validationError,
  isError,
  error,
  isSuccess,
  slots,
  selectedSlot,
  onSelectSlot,
}: QuickSlotResultsProps) {
  return (
    <>
      {(validationError || isError) && (
        <div
          role="alert"
          className="rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-2.5 text-xs font-medium text-rose-700"
        >
          {validationError ?? getErrorMessage(error, 'Không thể tra cứu khung giờ trống lúc này.')}
        </div>
      )}
      {!validationError && isSuccess && slots && (
        <div className="rounded-xl border border-sky-200 bg-sky-50/60 p-3.5 text-xs">
          {slots.length === 0 ? (
            <p className="font-medium text-slate-600">
              Hiện chưa có khung giờ trống cho lựa chọn này. Vui lòng chọn chuyên khoa hoặc bác sĩ
              khác.
            </p>
          ) : (
            <div className="space-y-2.5">
              <div className="font-bold text-sky-900">
                Đã tìm thấy {slots.length} khung giờ trống khả dụng:
              </div>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                {slots.map((slot) => (
                  <div
                    key={slot.id}
                    className="flex items-center justify-between rounded-lg border border-sky-200 bg-white p-2.5"
                  >
                    <div>
                      <div className="font-mono font-bold text-sky-700">{slot.timeRange}</div>
                      <div className="text-[11px] text-slate-600">
                        {slot.doctorName} • {slot.roomName}
                      </div>
                    </div>
                    <button
                      type="button"
                      onClick={() => onSelectSlot(slot)}
                      className="rounded-md bg-sky-600 px-2.5 py-1 text-[11px] font-bold text-white hover:bg-sky-700"
                    >
                      Chọn khung giờ
                    </button>
                  </div>
                ))}
              </div>
              {selectedSlot && (
                <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 font-semibold text-emerald-800">
                  Đã chọn khung giờ {selectedSlot.timeRange} cùng {selectedSlot.doctorName} tại{' '}
                  {selectedSlot.roomName} ({selectedSlot.feeLabel}).
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </>
  );
}
