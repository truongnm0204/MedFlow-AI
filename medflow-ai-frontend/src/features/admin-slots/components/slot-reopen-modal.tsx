import { useState } from 'react';
import { Calendar, CheckCircle2, Clock, RotateCcw, Stethoscope } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

interface SlotReopenModalProps {
  slot: ExaminationSlot | null;
  open: boolean;
  onClose: () => void;
  onSuccess?: (slot: ExaminationSlot, message: string) => void;
}

export function SlotReopenModal({ slot, open, onClose, onSuccess }: SlotReopenModalProps) {
  const reopenSlot = useExaminationSlotStore((state) => state.reopenSlot);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (!slot) return null;

  const handleConfirm = async () => {
    setIsSubmitting(true);
    setErrorMessage(null);
    try {
      const res = await reopenSlot(slot.id);
      onSuccess?.(slot, res.message);
      onClose();
    } catch (err) {
      setErrorMessage(
        err instanceof Error ? err.message : 'Không thể mở lại khung giờ khám. Vui lòng thử lại.',
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-lg">
      <div className="p-6 sm:p-7">
        {/* Header */}
        <div className="flex items-start gap-3.5 border-b border-border pb-4 pr-8">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-600">
            <RotateCcw className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-ink">Mở Lại Khung Giờ Khám</h2>
            <p className="mt-0.5 text-xs text-sub">
              Khung giờ sẽ chuyển về trạng thái hoạt động và cho phép bệnh nhân đặt lịch trở lại.
            </p>
          </div>
        </div>

        {/* Slot details card */}
        <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
          <div className="flex items-center justify-between border-b border-slate-200/60 pb-2.5">
            <span className="font-mono text-xs font-bold text-slate-800">{slot.code}</span>
            <span className="inline-flex items-center gap-1 text-[11px] font-medium text-rose-600">
              <span className="h-1.5 w-1.5 rounded-full bg-rose-600" />
              Đang đóng
            </span>
          </div>

          <div className="mt-3 space-y-1.5 text-xs">
            <div className="flex items-center gap-2 text-ink">
              <Stethoscope className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
              <span className="font-medium">{slot.doctorName}</span>
            </div>
            <div className="flex items-center gap-2 text-ink">
              <Clock className="h-3.5 w-3.5 shrink-0 text-slate-500" aria-hidden="true" />
              <span>
                {slot.startTime} - {slot.endTime} ({slot.dateDisplay})
              </span>
            </div>
            <div className="flex items-center gap-2 text-sub">
              <Calendar className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
              <span>
                {slot.serviceName} · Phòng {slot.room} · {slot.specialty}
              </span>
            </div>
          </div>

          {slot.closeReason && (
            <div className="mt-3 rounded-lg border border-slate-200/90 bg-white p-2.5 text-[11px] text-slate-600">
              <span className="font-semibold text-slate-800">Lý do đóng trước đó:</span>{' '}
              {slot.closeReason}
            </div>
          )}
        </div>

        {errorMessage && (
          <div className="mt-4 rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800">
            {errorMessage}
          </div>
        )}

        <div className="mt-6 flex items-center justify-end gap-2.5 border-t border-border pt-4">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={onClose}
            disabled={isSubmitting}
          >
            Hủy bỏ
          </Button>
          <Button
            type="button"
            variant="primary"
            size="sm"
            loading={isSubmitting}
            onClick={handleConfirm}
          >
            <CheckCircle2 className="h-3.5 w-3.5 mr-1" aria-hidden="true" />
            Xác nhận mở lại khung giờ
          </Button>
        </div>
      </div>
    </Modal>
  );
}
