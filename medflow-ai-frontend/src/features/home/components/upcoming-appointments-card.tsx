import { CheckCircle2, Clock, FileText } from 'lucide-react';

import { Badge } from '@/shared/ui';

import type { AppointmentStatus, UpcomingAppointmentItem } from '../types/home.types';

interface UpcomingAppointmentsCardProps {
  appointments: UpcomingAppointmentItem[];
}

const STATUS_BADGE: Record<
  AppointmentStatus,
  { label: string; tone: 'emerald' | 'amber' | 'sky' }
> = {
  CONFIRMED: { label: 'Đã xác nhận', tone: 'emerald' },
  PENDING: { label: 'Chờ xác nhận', tone: 'amber' },
  CHECKED_IN: { label: 'Đã tiếp nhận', tone: 'sky' },
};

/** Thẻ hiển thị danh sách lịch hẹn khám ngoại trú sắp tới của Patient. */
export function UpcomingAppointmentsCard({ appointments }: UpcomingAppointmentsCardProps) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-xs lg:col-span-5">
      <div className="mb-3 flex items-center justify-between border-b border-slate-100 pb-3">
        <h3 className="text-sm font-bold text-slate-900">Lịch hẹn khám sắp tới của bạn</h3>
        <FileText className="h-4 w-4 text-sky-600" />
      </div>

      {appointments.length === 0 ? (
        <div className="py-6 text-center">
          <p className="text-xs font-medium text-slate-500">
            Bạn chưa có lịch hẹn khám ngoại trú nào sắp tới.
          </p>
          <a
            href="#booking"
            className="mt-3 inline-block rounded-lg bg-sky-50 px-3 py-1.5 text-xs font-bold text-sky-700 hover:bg-sky-100"
          >
            Đặt lịch khám ngay
          </a>
        </div>
      ) : (
        <ul className="space-y-3">
          {appointments.map((apt) => {
            const badge = STATUS_BADGE[apt.status];
            return (
              <li
                key={apt.id}
                className="rounded-xl border border-slate-200 bg-slate-50/70 p-3.5 text-xs"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-mono font-bold text-sky-700">#{apt.appointmentCode}</span>
                  <Badge tone={badge.tone}>{badge.label}</Badge>
                </div>
                <div className="mt-1.5 font-bold text-slate-900">{apt.specialtyName}</div>
                <div className="mt-0.5 text-slate-600">
                  {apt.doctorName} • {apt.roomName}
                </div>
                <div className="mt-2 flex items-center gap-3 text-[11px] font-semibold text-slate-700">
                  <span className="inline-flex items-center gap-1">
                    <Clock className="h-3.5 w-3.5 text-sky-600" />
                    {apt.scheduledDate} ({apt.scheduledTime})
                  </span>
                  <span className="inline-flex items-center gap-1 text-emerald-700">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    {apt.patientName}
                  </span>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
