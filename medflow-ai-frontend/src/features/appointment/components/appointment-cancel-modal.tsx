import { useState } from 'react';
import { AlertTriangle, XCircle } from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import { cancelAppointmentSchema } from '../schemas/appointment-detail.schema';
import type { AppointmentListItem } from '../types/appointment-list.types';

interface AppointmentCancelModalProps {
  appointment: AppointmentListItem | null;
  isSubmitting?: boolean;
  onClose: () => void;
  onConfirmCancel: (cancellationReason: string) => void;
}

const QUICK_CANCEL_REASONS = [
  'Người bệnh bận công tác đột xuất, xin chủ động dời sang kíp khám sau.',
  'Triệu chứng lâm sàng đã thuyên giảm, chưa cần khám ngay trong hôm nay.',
  'Muốn đổi sang chuyên khoa hoặc bác sĩ phụ trách khác phù hợp hơn.',
];

export function AppointmentCancelModal({
  appointment,
  isSubmitting = false,
  onClose,
  onConfirmCancel,
}: AppointmentCancelModalProps) {
  const [reason, setReason] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  if (!appointment) return null;

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = cancelAppointmentSchema.safeParse({
      cancellationReason: reason,
    });

    if (!parsed.success) {
      setValidationError(parsed.error.issues[0]?.message ?? 'Vui lòng nhập lý do hủy lịch hợp lệ.');
      return;
    }

    setValidationError(null);
    onConfirmCancel(parsed.data.cancellationReason);
  };

  return (
    <Modal open={Boolean(appointment)} onClose={onClose} maxWidth="max-w-lg">
      <form onSubmit={handleSubmit} noValidate className="p-6">
        <div className="flex items-start gap-3 border-b border-border pb-4 pr-8">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-rose-50 text-danger">
            <XCircle className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-lg font-extrabold text-ink">Xác Nhận Hủy Lịch Hẹn Khám</h2>
            <p className="mt-0.5 text-xs text-sub">
              Mã hồ sơ tiếp nhận:{' '}
              <span className="font-mono font-bold text-ink">#{appointment.appointmentCode}</span> •{' '}
              {appointment.scheduledTimeRange} ({appointment.scheduledDateBadge})
            </p>
          </div>
        </div>

        <div className="mt-4 rounded-xl border border-amber-200 bg-amber-50/70 p-3.5 text-xs text-amber-900">
          <p className="flex items-center gap-1.5 font-bold">
            <AlertTriangle className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
            <span>Lưu ý giải phóng slot khám &amp; số thứ tự:</span>
          </p>
          <p className="mt-1 leading-relaxed">
            Sau khi xác nhận hủy, số thứ tự giữ trước{' '}
            <strong className="font-mono">STT {appointment.queueNumber ?? '012'}</strong> tại{' '}
            <strong>{appointment.roomName}</strong> sẽ được giải phóng ngay để phục vụ người bệnh
            khác. Không phát sinh chi phí hủy lịch.
          </p>
        </div>

        <div className="mt-4">
          <p className="text-xs font-semibold text-sub">Chọn nhanh lý do phổ biến:</p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {QUICK_CANCEL_REASONS.map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => {
                  setReason(preset);
                  setValidationError(null);
                }}
                className="rounded-lg border border-border bg-canvas px-2.5 py-1.5 text-left text-[11px] font-medium text-ink transition-colors hover:border-primary hover:bg-sky-50"
              >
                {preset}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-4">
          <label htmlFor="cancel-reason-textarea" className="block text-xs font-bold text-ink">
            Lý do hủy lịch hẹn <span className="text-danger">*</span>
          </label>
          <textarea
            id="cancel-reason-textarea"
            rows={3}
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
              if (validationError) setValidationError(null);
            }}
            placeholder="Nhập lý do hủy lịch hẹn (tối thiểu 10 ký tự)..."
            className="mt-1.5 w-full rounded-xl border border-border bg-white p-3 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:outline-none"
          />
          {validationError && (
            <p role="alert" className="mt-1.5 text-xs font-semibold text-danger">
              {validationError}
            </p>
          )}
        </div>

        <div className="mt-5 flex items-center justify-end gap-3 border-t border-border pt-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-border bg-white px-4 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
          >
            Quay lại
          </button>
          <button
            type="submit"
            disabled={isSubmitting}
            className="rounded-xl bg-rose-600 px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-rose-700 disabled:opacity-60"
          >
            {isSubmitting ? 'Đang hủy lịch...' : 'Xác nhận hủy lịch hẹn'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
