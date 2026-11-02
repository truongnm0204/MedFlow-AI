import { QrCode } from 'lucide-react';

interface AppointmentCheckinQrProps {
  appointmentCode: string;
  caption: string;
  size?: 'md' | 'lg';
}

export function AppointmentCheckinQr({
  appointmentCode,
  caption,
  size = 'md',
}: AppointmentCheckinQrProps) {
  const iconClassName = size === 'lg' ? 'h-28 w-28 text-ink' : 'h-12 w-12 text-ink';

  return (
    <div
      role="img"
      aria-label={`Mã QR check-in #${appointmentCode}`}
      className="flex flex-col items-center justify-center rounded-xl border border-border bg-white p-2.5"
    >
      <QrCode className={iconClassName} aria-hidden="true" />
      <span className="mt-1 text-[10px] font-semibold text-sub">{caption}</span>
    </div>
  );
}
