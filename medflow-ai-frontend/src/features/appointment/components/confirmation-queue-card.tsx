import { Clock } from 'lucide-react';

import type { AppointmentListItem } from '../types/appointment-list.types';
import { AppointmentCheckinQr } from './appointment-checkin-qr';

interface ConfirmationQueueCardProps {
  appointment: AppointmentListItem;
}

export function ConfirmationQueueCard({ appointment }: ConfirmationQueueCardProps) {
  const roomHeaderLabel = appointment.roomCode.replace('P.', 'PHÒNG KHÁM ');
  const counterLabel = appointment.checkInCounterLabel ?? 'Quầy Tiếp đón Tầng 1 - Nhà A';

  return (
    <section
      aria-label="Số thứ tự giữ trước"
      className="rounded-2xl border-2 border-sky-200 bg-sky-50/60 p-6 text-center"
    >
      <span className="inline-block rounded-full border border-sky-200 bg-white px-3.5 py-1 text-xs font-bold text-primary uppercase">
        SỐ THỨ TỰ (STT) GIỮ TRƯỚC TẠI {roomHeaderLabel}
      </span>

      <p className="mt-3 font-mono text-5xl font-extrabold text-primary sm:text-6xl">
        STT: {appointment.queueNumber ?? '---'}
      </p>

      <p className="mt-2 inline-flex flex-wrap items-center justify-center gap-1.5 text-xs text-sub sm:text-sm">
        <Clock className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <span>Khung giờ:</span>
        <strong className="font-bold text-ink">{appointment.scheduledTimeRange}</strong>
        <span>•</span>
        <span>{appointment.scheduledDateFullLabel}</span>
      </p>

      <div className="my-5 border-t border-sky-200" />

      <div className="mx-auto max-w-48">
        <AppointmentCheckinQr
          appointmentCode={appointment.appointmentCode}
          size="lg"
          caption={`CHECK-IN: #${appointment.appointmentCode}`}
        />
      </div>

      <p className="mt-3 text-xs text-sub">
        Quét mã này tại {counterLabel} hoặc Kiosk tự phục vụ để in phiếu vào buồng khám.
      </p>
    </section>
  );
}
