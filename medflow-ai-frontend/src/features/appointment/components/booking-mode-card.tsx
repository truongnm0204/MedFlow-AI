import { ClipboardList, Lock, Stethoscope } from 'lucide-react';

import type { BookingMode } from '../types/booking-entry.types';

interface BookingModeCardProps {
  mode: BookingMode;
  disabled: boolean;
  onSelect: (mode: BookingMode) => void;
}

const MODE_CONTENT: Record<
  BookingMode,
  {
    icon: typeof Stethoscope;
    iconBox: string;
    title: string;
    description: string;
    steps: string;
    cta: string;
  }
> = {
  DOCTOR: {
    icon: Stethoscope,
    iconBox: 'bg-sky-50 text-primary',
    title: 'Khám theo Bác sĩ',
    description:
      'Chủ động chọn bác sĩ chuyên khoa bạn tin tưởng, xem lịch trực và đặt đúng khung giờ.',
    steps: 'Chọn bác sĩ › Xem lịch trực › Chọn ngày & giờ',
    cta: 'Chọn bác sĩ →',
  },
  SERVICE: {
    icon: ClipboardList,
    iconBox: 'bg-emerald-50 text-emerald-600',
    title: 'Khám theo Dịch vụ',
    description: 'Chọn gói/dịch vụ khám, hệ thống tự phân bổ bác sĩ phù hợp theo ca khám.',
    steps: 'Chọn dịch vụ › Chọn ngày & ca khám',
    cta: 'Chọn dịch vụ →',
  },
};

export function BookingModeCard({ mode, disabled, onSelect }: BookingModeCardProps) {
  const content = MODE_CONTENT[mode];
  const Icon = content.icon;

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => onSelect(mode)}
      className={`rounded-2xl border p-4 text-left transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-60 ${
        disabled
          ? 'border-dashed border-border bg-canvas'
          : 'border-border bg-white shadow-sm hover:border-primary hover:ring-2 hover:ring-sky-100'
      }`}
    >
      <div className="flex items-start gap-3">
        <span
          className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${content.iconBox}`}
        >
          <Icon className="h-5 w-5" aria-hidden="true" />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-bold text-ink">{content.title}</p>
          <p className="mt-1 text-xs leading-relaxed text-sub">{content.description}</p>
          <p className="mt-2 text-[11px] font-semibold text-slate-500">{content.steps}</p>
        </div>
      </div>

      <div className="mt-3 flex items-center justify-between gap-2">
        {disabled && (
          <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-slate-500">
            <Lock className="h-3 w-3" aria-hidden="true" />
            Chọn cơ sở trước
          </span>
        )}
        {!disabled && <span />}
        <span
          className={`inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold ${
            disabled ? 'bg-slate-200 text-slate-500' : 'bg-primary text-white'
          }`}
        >
          {content.cta}
        </span>
      </div>
    </button>
  );
}
