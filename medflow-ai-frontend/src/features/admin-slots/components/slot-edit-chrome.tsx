import { Clock, Save } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import type { ExaminationSlot } from '../types/examination-slot.types';

export function SlotEditHeader({
  slot,
  status,
  onClose,
}: {
  slot: ExaminationSlot;
  status: string;
  onClose: () => void;
}) {
  const isClosed = status === 'CLOSED';
  const isFull = status === 'FULL';

  return (
    <header className="flex items-center justify-between border-b border-border bg-slate-50 px-6 py-4">
      <div className="flex min-w-0 items-center gap-3">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-lg border border-sky-100 bg-sky-50">
          <Clock className="h-5 w-5 text-primary" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="slot-edit-title" className="text-base font-bold text-ink truncate">
              Khung giờ Khám - {slot.code}
            </h2>
            <span className="rounded border border-sky-200 bg-sky-50 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">
              {slot.serviceCode}
            </span>
            <span
              className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${
                isClosed
                  ? 'border-rose-200 bg-rose-50 text-rose-700'
                  : isFull
                    ? 'border-amber-200 bg-amber-50 text-amber-700'
                    : 'border-emerald-200 bg-emerald-50 text-emerald-700'
              }`}
            >
              {isClosed ? '● Đã đóng' : isFull ? '● Hết chỗ' : '● Đang mở'}
            </span>
          </div>
          <p className="mt-0.5 text-[11px] text-sub">
            Mã màn hình: SCR-ADM-SLT-03 • Quản lý khung giờ tiếp nhận, phòng khám và sức chứa lượt
            khám.
          </p>
        </div>
      </div>
      <button
        type="button"
        aria-label="Đóng chỉnh sửa"
        className="ml-3 text-xl text-slate-400 hover:text-ink cursor-pointer"
        onClick={onClose}
      >
        ×
      </button>
    </header>
  );
}

export function SlotEditFooter({
  isOpen,
  onClose,
  onToggleStatus,
}: {
  isOpen: boolean;
  onClose: () => void;
  onToggleStatus: () => void;
}) {
  return (
    <footer className="flex flex-wrap items-center justify-between gap-2 border-t border-border bg-slate-50 px-6 py-4">
      <button
        type="button"
        className={`rounded border px-3 py-2 text-[11px] font-semibold transition-colors cursor-pointer ${
          isOpen
            ? 'border-rose-200 text-rose-600 hover:bg-rose-50'
            : 'border-emerald-200 text-emerald-700 hover:bg-emerald-50'
        }`}
        onClick={onToggleStatus}
      >
        {isOpen ? '⊖ Đóng khung giờ' : '⊕ Mở lại khung giờ'}
      </button>
      <div className="flex gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onClose}>
          Hủy bỏ [Esc]
        </Button>
        <Button id="slot-edit-submit" type="submit" size="sm" className="gap-1.5 font-bold">
          <Save className="h-4 w-4" aria-hidden="true" />
          Lưu khung giờ khám [Ctrl+S]
        </Button>
      </div>
    </footer>
  );
}
