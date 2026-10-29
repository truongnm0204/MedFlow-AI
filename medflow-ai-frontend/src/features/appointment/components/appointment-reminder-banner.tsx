import { useState } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Clock,
  MapPin,
  Stethoscope,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { AppointmentReminder } from '../types/appointment-reminder.types';
import { AppointmentReminderModal } from './appointment-reminder-modal';

interface AppointmentReminderBannerProps {
  reminder?: AppointmentReminder;
}

export function AppointmentReminderBanner({ reminder }: AppointmentReminderBannerProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  if (!reminder) return null;

  const isUrgent = reminder.urgency === 'URGENT';

  return (
    <>
      <section
        aria-label="Thông báo nhắc lịch khám"
        className={`relative overflow-hidden rounded-3xl border transition-all shadow-xs ${
          isUrgent
            ? 'border-amber-200/90 bg-gradient-to-r from-amber-50/90 via-orange-50/50 to-amber-50/70 text-slate-800'
            : 'border-sky-200/90 bg-gradient-to-r from-sky-50/90 via-indigo-50/40 to-sky-50/70 text-slate-800'
        }`}
      >
        <div className="p-5 sm:p-6 flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex items-start sm:items-center gap-4">
            <div
              className={`w-12 h-12 rounded-2xl flex items-center justify-center shrink-0 shadow-xs border ${
                isUrgent
                  ? 'bg-amber-100/80 border-amber-300 text-amber-700'
                  : 'bg-sky-100/80 border-sky-300 text-sky-700'
              }`}
            >
              {isUrgent ? (
                <AlertTriangle className="w-6 h-6 animate-bounce" aria-hidden="true" />
              ) : (
                <Clock className="w-6 h-6" aria-hidden="true" />
              )}
            </div>

            <div className="space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <Badge tone={isUrgent ? 'amber' : 'sky'}>
                  {isUrgent
                    ? `NHẮC LỊCH KHẨN CẤP • CÒN ${reminder.hoursRemaining} GIỜ`
                    : 'NHẮC HẸN SẮP TỚI'}
                </Badge>
                <span className="font-mono text-xs font-bold text-slate-600">
                  {reminder.appointmentCode}
                </span>
                <span className="text-[11px] text-slate-400">• {reminder.notifiedAt}</span>
              </div>

              <h2 className="text-base sm:text-lg font-bold text-slate-900 tracking-tight">
                Lịch khám {reminder.specialtyName} lúc {reminder.scheduledTime} hôm nay
              </h2>

              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-600 font-medium">
                <span className="flex items-center gap-1.5">
                  <Stethoscope className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
                  {reminder.doctorName}
                </span>
                <span className="flex items-center gap-1.5">
                  <MapPin className="w-3.5 h-3.5 text-emerald-600" aria-hidden="true" />
                  {reminder.roomName}
                </span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2.5 self-end md:self-center shrink-0">
            <Button
              variant={isUrgent ? 'primary' : 'secondary'}
              size="sm"
              onClick={() => setIsModalOpen(true)}
              className="gap-2 shadow-xs"
            >
              <span>Xem hướng dẫn & Lưu ý</span>
              <ArrowRight className="w-4 h-4" aria-hidden="true" />
            </Button>
          </div>
        </div>
      </section>

      <AppointmentReminderModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        reminder={reminder}
      />
    </>
  );
}
