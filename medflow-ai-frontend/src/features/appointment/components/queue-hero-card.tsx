import { Building2, FileText, Stethoscope } from 'lucide-react';

import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

interface QueueHeroCardProps {
  data: AppointmentQueueStatus;
}

export function QueueHeroCard({ data }: QueueHeroCardProps) {
  const isCompleted = data.status === 'COMPLETED';
  const statusPillClass = isCompleted
    ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
    : 'border-amber-200 bg-amber-50 text-amber-800';

  return (
    <section
      aria-label="Số thứ tự khám ngoại trú của bạn"
      className="rounded-2xl border border-sky-200 bg-linear-to-b from-sky-50 to-white p-8 text-center shadow-xs"
    >
      <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-white px-3.5 py-1 font-mono text-xs font-bold text-primary">
        <FileText className="h-3.5 w-3.5" aria-hidden="true" />
        <span>
          MÃ CA KHÁM: #{data.visitCode} • MÃ HẸN: #{data.appointmentCode}
        </span>
      </span>

      <p className="mt-3 text-xs font-bold tracking-wider text-sub uppercase">
        SỐ THỨ TỰ KHÁM NGOẠI TRÚ CỦA BẠN
      </p>

      <p className="mt-2 font-mono text-5xl font-extrabold text-primary sm:text-6xl">
        STT CỦA BẠN: {data.queueNumber}
      </p>

      <div className="mt-3">
        <span
          className={`inline-flex items-center rounded-full border px-3.5 py-1 text-xs font-extrabold uppercase ${statusPillClass}`}
        >
          QUEUE: {data.statusLabel}
        </span>
      </div>

      <div className="mt-5 flex flex-wrap items-center justify-center gap-3 text-xs font-semibold text-ink sm:text-sm">
        <span className="inline-flex items-center gap-1.5">
          <Building2 className="h-4 w-4 text-primary" aria-hidden="true" />
          <span>{data.roomName}</span>
        </span>
        <span className="text-sub" aria-hidden="true">
          •
        </span>
        <span className="inline-flex items-center gap-1.5">
          <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
          <span>Bác sĩ: {data.doctorName}</span>
        </span>
      </div>

      <p className="mt-1 text-xs text-sub">{data.departmentName}</p>
    </section>
  );
}
