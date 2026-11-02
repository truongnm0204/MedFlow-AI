import { Link } from 'react-router-dom';
import { Info } from 'lucide-react';

import type { AppointmentListItem } from '../types/appointment-list.types';

interface ConfirmationArrivalGuideProps {
  appointment: AppointmentListItem;
}

export function ConfirmationArrivalGuide({ appointment }: ConfirmationArrivalGuideProps) {
  const deadline = appointment.checkInDeadlineTime ?? '--:--';
  const counter = appointment.checkInCounterLabel ?? 'Quầy Tiếp đón Tầng 1 - Nhà A';

  return (
    <section
      aria-label="Hướng dẫn tiếp đón khi đến bệnh viện"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex items-center gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
          <Info className="h-5 w-5" aria-hidden="true" />
        </div>
        <h2 className="text-sm font-bold text-ink uppercase">
          Hướng Dẫn Tiếp Đón Khi Đến Bệnh Viện
        </h2>
      </div>

      <ol className="mt-4 space-y-3 text-xs leading-relaxed text-ink">
        <li className="flex items-start gap-3 rounded-xl border border-border bg-canvas p-3.5">
          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-white">
            1
          </span>
          <p>
            Có mặt lúc <strong>{deadline}</strong> (trước giờ hẹn 15 phút) tại{' '}
            <strong>{counter}</strong> để đối chiếu thông tin và hoàn tất thủ tục ban đầu.
          </p>
        </li>

        <li className="flex items-start gap-3 rounded-xl border border-border bg-canvas p-3.5">
          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-white">
            2
          </span>
          <p>
            Xuất trình mã hẹn #{appointment.appointmentCode} hoặc đưa mã QR tại quầy nhân viên tiếp
            đón để in phiếu vào buồng khám.
          </p>
        </li>

        <li className="flex items-start gap-3 rounded-xl border border-border bg-canvas p-3.5">
          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-white">
            3
          </span>
          <p>
            Di chuyển lên <strong>{appointment.roomName}</strong>, ngồi tại sảnh chờ và theo dõi
            bảng điện tử gọi số STT{' '}
            <strong className="font-mono">{appointment.queueNumber ?? '---'}</strong> để vào khám
            đúng lượt.{' '}
            <Link
              to={`/appointments/${appointment.id}/queue`}
              className="font-bold text-primary underline hover:text-primary-dark"
            >
              Theo dõi hàng đợi trực tiếp
            </Link>
          </p>
        </li>
      </ol>
    </section>
  );
}
