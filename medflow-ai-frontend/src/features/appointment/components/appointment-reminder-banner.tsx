import { AlertTriangle, ArrowRight, Clock, MapPin, Stethoscope } from 'lucide-react';
import { useState } from 'react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { useNotificationsQuery } from '../hooks/use-notifications';
import type { AppointmentReminder } from '../types/appointment-reminder.types';
import { AppointmentReminderModal } from './appointment-reminder-modal';

interface AppointmentReminderBannerProps {
  reminder?: AppointmentReminder | null;
  className?: string;
}

function formatScheduledDatePhrase(scheduledDate: string): string {
  const trimmed = scheduledDate.trim();
  const nowVnIso = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());

  if (trimmed === nowVnIso) {
    return 'hôm nay';
  }

  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (match) {
    return `ngày ${match[3]}/${match[2]}/${match[1]}`;
  }

  return trimmed;
}

function AppointmentReminderBannerView({
  reminder,
  className = '',
}: {
  reminder: AppointmentReminder;
  className?: string;
}) {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const isUrgent = reminder.urgency === 'URGENT';
  const datePhrase = formatScheduledDatePhrase(reminder.scheduledDate);

  return (
    <>
      <section
        aria-label="Thông báo nhắc lịch khám"
        className={`relative overflow-hidden rounded-3xl border shadow-xs transition-all ${
          isUrgent
            ? 'border-amber-200/90 bg-gradient-to-r from-amber-50/90 via-orange-50/50 to-amber-50/70 text-ink'
            : 'border-sky-200/90 bg-gradient-to-r from-sky-50/90 via-indigo-50/40 to-sky-50/70 text-ink'
        } ${className}`.trim()}
      >
        <div className="flex flex-col justify-between gap-4 p-5 sm:p-6 md:flex-row md:items-center">
          <div className="flex items-start gap-4 sm:items-center">
            <div
              className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border shadow-xs ${
                isUrgent
                  ? 'border-amber-300 bg-amber-100/80 text-amber-700'
                  : 'border-sky-300 bg-sky-100/80 text-sky-700'
              }`}
            >
              {isUrgent ? (
                <AlertTriangle
                  className="h-6 w-6 animate-bounce motion-reduce:animate-none"
                  aria-hidden="true"
                />
              ) : (
                <Clock className="h-6 w-6" aria-hidden="true" />
              )}
            </div>

            <div className="space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <Badge tone={isUrgent ? 'amber' : 'sky'}>
                  {isUrgent
                    ? `NHẮC LỊCH KHẨN CẤP • CÒN ${reminder.hoursRemaining} GIỜ`
                    : 'NHẮC HẸN SẮP TỚI'}
                </Badge>
                <span className="font-mono text-xs font-bold text-sub">
                  {reminder.appointmentCode}
                </span>
                <span className="text-[11px] text-sub">• {reminder.notifiedAt}</span>
              </div>

              <h2 className="text-base font-bold tracking-tight text-ink sm:text-lg">
                Lịch khám {reminder.specialtyName} lúc {reminder.scheduledTime} {datePhrase}
              </h2>

              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs font-medium text-sub">
                <span className="flex items-center gap-1.5">
                  <Stethoscope className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                  {reminder.doctorName}
                </span>
                <span className="flex items-center gap-1.5">
                  <MapPin className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                  {reminder.roomName}
                </span>
              </div>
            </div>
          </div>

          <div className="flex shrink-0 items-center gap-2.5 self-end md:self-center">
            <Button
              variant={isUrgent ? 'primary' : 'secondary'}
              size="sm"
              onClick={() => setIsModalOpen(true)}
              className="gap-2 shadow-xs"
            >
              <span>Xem hướng dẫn & Lưu ý</span>
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
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

function LiveAppointmentReminderBanner({ className }: { className?: string }) {
  const { data } = useNotificationsQuery();
  if (!data?.activeReminder) {
    return null;
  }

  return <AppointmentReminderBannerView reminder={data.activeReminder} className={className} />;
}

export function AppointmentReminderBanner({ reminder, className }: AppointmentReminderBannerProps) {
  if (reminder !== undefined) {
    if (!reminder) return null;
    return <AppointmentReminderBannerView reminder={reminder} className={className} />;
  }

  return <LiveAppointmentReminderBanner className={className} />;
}
