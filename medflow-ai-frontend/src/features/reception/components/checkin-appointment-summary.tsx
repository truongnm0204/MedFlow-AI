import { Calendar, Clock, MapPin, Phone, ShieldCheck, Stethoscope, User } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { ScheduledCheckInAppointment } from '../types/checkin.types';

interface CheckInAppointmentSummaryProps {
  appointment: ScheduledCheckInAppointment;
}

const GENDER_LABEL: Record<string, string> = {
  MALE: 'Nam',
  FEMALE: 'Nữ',
  OTHER: 'Khác',
};

const CURRENT_YEAR = new Date().getFullYear();

export function CheckInAppointmentSummary({ appointment }: CheckInAppointmentSummaryProps) {
  const age = CURRENT_YEAR - appointment.birthYear;

  return (
    <div className="p-4.5 rounded-2xl bg-canvas border border-border space-y-4">
      {/* Top row */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200/80 pb-3">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-bold text-sky-700 bg-sky-50 px-2.5 py-1 rounded-lg border border-sky-200">
            {appointment.appointmentCode}
          </span>
          <Badge tone="sky">HẸN TRƯỚC</Badge>
        </div>
        <div className="flex items-center gap-1.5 text-xs font-bold text-slate-800">
          <Clock className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
          <span>{appointment.time}</span>
          <span className="text-slate-400 font-normal">•</span>
          <Calendar className="w-3.5 h-3.5 text-slate-400" aria-hidden="true" />
          <span className="text-slate-600 font-normal">{appointment.date}</span>
        </div>
      </div>

      {/* Grid details */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
        {/* Patient */}
        <div className="space-y-1">
          <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 flex items-center gap-1">
            <User className="w-3 h-3" aria-hidden="true" /> Bệnh nhân
          </div>
          <div className="font-bold text-sm text-slate-900">{appointment.patientName}</div>
          <div className="text-slate-500 font-mono text-[11px]">
            {appointment.patientCode} • {GENDER_LABEL[appointment.gender]} ({age} tuổi)
          </div>
        </div>

        {/* Doctor & Room */}
        <div className="space-y-1">
          <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 flex items-center gap-1">
            <Stethoscope className="w-3 h-3 text-primary" aria-hidden="true" /> Khám chỉ định
          </div>
          <div className="font-bold text-sm text-slate-900">{appointment.doctorName}</div>
          <div className="text-slate-600 flex items-center gap-1.5 text-[11px]">
            <span>{appointment.department}</span>
            <span className="text-slate-400">•</span>
            <MapPin className="w-3 h-3 text-emerald-600 shrink-0" aria-hidden="true" />
            <span className="font-semibold text-emerald-700">{appointment.roomName}</span>
          </div>
        </div>

        {/* Identity & Phone */}
        <div className="space-y-0.5 pt-1">
          <div className="text-slate-500 text-[11px]">CCCD / VNeID:</div>
          <div className="font-mono font-medium text-slate-800">{appointment.identityCard}</div>
          <div className="text-slate-500 flex items-center gap-1 text-[11px]">
            <Phone className="w-3 h-3 text-slate-400" aria-hidden="true" />
            {appointment.phoneNumber}
          </div>
        </div>

        {/* Health Insurance */}
        <div className="space-y-0.5 pt-1">
          <div className="text-slate-500 text-[11px]">Bảo hiểm y tế:</div>
          {appointment.healthInsuranceNumber ? (
            <div className="flex items-center gap-1.5 font-mono text-emerald-700 font-semibold">
              <ShieldCheck className="w-3.5 h-3.5 text-emerald-600" aria-hidden="true" />
              {appointment.healthInsuranceNumber}
            </div>
          ) : (
            <div className="text-slate-400 italic text-[11px]">Khám tự nguyện (Không BHYT)</div>
          )}
        </div>
      </div>
    </div>
  );
}
