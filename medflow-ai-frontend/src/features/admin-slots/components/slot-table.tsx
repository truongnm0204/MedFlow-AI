import {
  Ban,
  Calendar,
  CheckCircle2,
  Eye,
  Info,
  Pencil,
  RotateCcw,
  Stethoscope,
} from 'lucide-react';

import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

interface SlotTableProps {
  slots: ExaminationSlot[];
  onEditSlot?: (slot: ExaminationSlot) => void;
  onViewSchedule?: (slot: ExaminationSlot) => void;
  onViewDetail?: (slot: ExaminationSlot) => void;
  onRequestCloseSlot?: (slot: ExaminationSlot) => void;
  onRequestReopenSlot?: (slot: ExaminationSlot) => void;
  onRequestBatchClose?: (selectedSlots: ExaminationSlot[]) => void;
}

export function SlotTable({
  slots,
  onEditSlot,
  onViewSchedule,
  onViewDetail,
  onRequestCloseSlot,
  onRequestReopenSlot,
  onRequestBatchClose,
}: SlotTableProps) {
  const selectedIds = useExaminationSlotStore((state) => state.selectedIds);
  const toggleSelection = useExaminationSlotStore((state) => state.toggleSelection);
  const toggleAll = useExaminationSlotStore((state) => state.toggleAll);
  const clearSelection = useExaminationSlotStore((state) => state.clearSelection);
  const batchUpdateStatus = useExaminationSlotStore((state) => state.batchUpdateStatus);

  const visibleIds = slots.map((s) => s.id);
  const allSelected = visibleIds.length > 0 && visibleIds.every((id) => selectedIds.includes(id));

  return (
    <div className="overflow-x-auto">
      {/* Batch selection action bar */}
      {selectedIds.length > 0 && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-primary/20 bg-sky-50/80 px-6 py-2.5 text-xs text-primary font-medium">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>
              Đã chọn <b>{selectedIds.length}</b> khung giờ
            </span>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => batchUpdateStatus('OPEN')}
              className="rounded-md border border-emerald-200 bg-white px-2.5 py-1 text-emerald-700 shadow-2xs hover:bg-emerald-50 transition-colors"
            >
              Mở hàng loạt
            </button>
            <button
              type="button"
              onClick={() => {
                if (onRequestBatchClose) {
                  const selected = slots.filter((s) => selectedIds.includes(s.id));
                  onRequestBatchClose(selected);
                } else {
                  batchUpdateStatus('CLOSED');
                }
              }}
              className="rounded-md border border-rose-200 bg-white px-2.5 py-1 text-rose-700 shadow-2xs hover:bg-rose-50 transition-colors"
            >
              Đóng hàng loạt
            </button>
            <button
              type="button"
              onClick={clearSelection}
              className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-slate-600 shadow-2xs hover:bg-slate-50 transition-colors"
            >
              Bỏ chọn
            </button>
          </div>
        </div>
      )}

      <table className="w-full min-w-[1200px] table-fixed text-left text-xs">
        <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wider text-sub">
          <tr>
            <th className="w-10 px-4 py-3.5">
              <input
                aria-label="Chọn tất cả khung giờ trên trang"
                type="checkbox"
                checked={allSelected}
                onChange={() => toggleAll(visibleIds)}
                className="rounded border-slate-300 text-primary focus:ring-primary"
              />
            </th>
            <th className="w-[10%] px-3">MÃ SLOT</th>
            <th className="w-[24%] px-3">LỊCH / BÁC SĨ</th>
            <th className="w-[11%] px-3">LOẠI DỊCH VỤ</th>
            <th className="w-[13%] px-3">CHUYÊN KHOA</th>
            <th className="w-[10%] px-3">PHÒNG</th>
            <th className="w-[11%] px-3">SỨC CHỨA</th>
            <th className="w-[13%] px-3">NGÀY / GIỜ</th>
            <th className="w-[10%] px-3">TRẠNG THÁI</th>
            <th className="w-[8%] px-3 text-right pr-5">THAO TÁC</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 bg-white">
          {slots.length === 0 ? (
            <tr>
              <td colSpan={10} className="py-16 text-center">
                <div className="mx-auto flex max-w-sm flex-col items-center">
                  <div className="grid h-12 w-12 place-items-center rounded-full bg-slate-100 text-slate-400">
                    <Info className="h-6 w-6" aria-hidden="true" />
                  </div>
                  <p className="mt-3 text-sm font-semibold text-ink">
                    Không tìm thấy khung giờ nào
                  </p>
                  <p className="mt-1 text-xs text-sub">
                    Thử tìm kiếm với từ khóa khác hoặc xóa bớt các bộ lọc đang áp dụng.
                  </p>
                </div>
              </td>
            </tr>
          ) : (
            slots.map((slot) => {
              const isSelected = selectedIds.includes(slot.id);
              const isFull = slot.capacityBooked >= slot.capacityMax && slot.capacityMax > 0;
              const isClosed = slot.status === 'CLOSED';

              return (
                <tr
                  key={slot.id}
                  className={`transition-colors hover:bg-slate-50/80 ${
                    isSelected ? 'bg-sky-50/40' : ''
                  }`}
                >
                  {/* Selection Checkbox */}
                  <td className="px-4 py-3.5">
                    <input
                      aria-label={`Chọn khung giờ ${slot.code}`}
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => toggleSelection(slot.id)}
                      className="rounded border-slate-300 text-primary focus:ring-primary"
                    />
                  </td>

                  {/* Mã Slot */}
                  <td className="px-3 py-3.5 font-mono text-xs font-semibold text-slate-800">
                    {slot.code}
                  </td>

                  {/* Lịch / Bác sĩ */}
                  <td className="px-3 py-3.5">
                    <div className="flex items-start gap-2">
                      <span className="mt-0.5 text-primary" aria-hidden="true">
                        <Stethoscope className="h-3.5 w-3.5" />
                      </span>
                      <div className="min-w-0">
                        <p className="truncate font-bold text-ink">{slot.doctorName}</p>
                        <p className="truncate text-[11px] text-sub">
                          {slot.serviceName} · <span className="font-mono">{slot.serviceCode}</span>
                        </p>
                      </div>
                    </div>
                  </td>

                  {/* Loại dịch vụ */}
                  <td className="px-3 py-3.5">
                    <span
                      className={`inline-block rounded-md border px-2 py-0.5 text-[11px] font-medium ${
                        slot.serviceType === 'DIAGNOSTIC'
                          ? 'border-teal-200 bg-teal-50 text-teal-700'
                          : 'border-slate-200 bg-slate-50 text-slate-700'
                      }`}
                    >
                      {slot.serviceType === 'DIAGNOSTIC' ? 'Cận lâm sàng' : 'Ngoại trú'}
                    </span>
                  </td>

                  {/* Chuyên khoa */}
                  <td className="px-3 py-3.5 text-ink">{slot.specialty}</td>

                  {/* Phòng */}
                  <td className="px-3 py-3.5 font-mono text-ink">{slot.room}</td>

                  {/* Sức chứa */}
                  <td className="px-3 py-3.5">
                    <div className="flex items-center gap-1.5">
                      <span
                        className={`font-mono text-xs font-bold ${
                          isFull
                            ? 'text-amber-600'
                            : slot.capacityBooked > 0
                              ? 'text-ink'
                              : 'text-sub'
                        }`}
                      >
                        {String(slot.capacityBooked).padStart(2, '0')} /{' '}
                        {String(slot.capacityMax).padStart(2, '0')}
                      </span>
                      <span
                        className="text-slate-400"
                        title={`Đã đặt ${slot.capacityBooked}/${slot.capacityMax} chỗ`}
                      >
                        <Info className="h-3 w-3" aria-hidden="true" />
                      </span>
                    </div>
                  </td>

                  {/* Ngày / Giờ */}
                  <td className="px-3 py-3.5">
                    <div className="flex items-center gap-1.5 text-ink font-medium">
                      <span className="text-slate-400">·</span>
                      <span>
                        {slot.dateDisplay} - {slot.startTime}
                      </span>
                    </div>
                  </td>

                  {/* Trạng thái */}
                  <td className="px-3 py-3.5">
                    {isClosed ? (
                      <span className="inline-flex items-center gap-1.5 text-xs font-medium text-rose-600">
                        <span className="h-1.5 w-1.5 rounded-full bg-rose-600" aria-hidden="true" />
                        <span>Đã đóng</span>
                      </span>
                    ) : isFull ? (
                      <span className="inline-flex items-center gap-1.5 text-xs font-medium text-amber-600">
                        <span
                          className="h-1.5 w-1.5 rounded-full bg-amber-600"
                          aria-hidden="true"
                        />
                        <span>Đang mở</span>
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 text-xs font-medium text-emerald-600">
                        <span
                          className="h-1.5 w-1.5 rounded-full bg-emerald-600"
                          aria-hidden="true"
                        />
                        <span>Đang mở</span>
                      </span>
                    )}
                  </td>

                  {/* Thao tác */}
                  <td className="px-3 py-3.5 text-right pr-5">
                    <div className="flex items-center justify-end gap-1 text-slate-500">
                      {isClosed ? (
                        <button
                          type="button"
                          onClick={() => onRequestReopenSlot?.(slot)}
                          className="rounded p-1 text-slate-500 hover:bg-emerald-50 hover:text-emerald-600 transition-colors"
                          title="Mở lại khung giờ"
                          aria-label={`Mở lại khung giờ ${slot.code}`}
                        >
                          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                        </button>
                      ) : (
                        <button
                          type="button"
                          onClick={() => onRequestCloseSlot?.(slot)}
                          className="rounded p-1 text-slate-500 hover:bg-rose-50 hover:text-rose-600 transition-colors"
                          title="Đóng khung giờ"
                          aria-label={`Đóng khung giờ ${slot.code}`}
                        >
                          <Ban className="h-3.5 w-3.5" aria-hidden="true" />
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => onEditSlot?.(slot)}
                        className="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-ink transition-colors"
                        title="Chỉnh sửa khung giờ"
                        aria-label={`Chỉnh sửa khung giờ ${slot.code}`}
                      >
                        <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        onClick={() => onViewSchedule?.(slot)}
                        className="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-ink transition-colors"
                        title="Xem lịch làm việc"
                        aria-label={`Xem lịch làm việc của ${slot.doctorName}`}
                      >
                        <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        onClick={() => onViewDetail?.(slot)}
                        className="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-ink transition-colors"
                        title="Xem chi tiết"
                        aria-label={`Xem chi tiết khung giờ ${slot.code}`}
                      >
                        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
