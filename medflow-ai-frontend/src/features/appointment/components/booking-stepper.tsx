import { Check } from 'lucide-react';

interface BookingStepItem {
  stepNumber: number;
  label: string;
  title: string;
  status: 'completed' | 'current' | 'upcoming';
}

interface BookingStepperProps {
  mode?: 'doctor' | 'service';
}

export function BookingStepper({ mode = 'doctor' }: BookingStepperProps) {
  const steps: BookingStepItem[] = [
    {
      stepNumber: 1,
      label: 'BƯỚC 1',
      title: mode === 'service' ? 'Dịch vụ khám' : 'Khoa & Bác sĩ',
      status: 'completed',
    },
    {
      stepNumber: 2,
      label: 'BƯỚC 2 — HIỆN TẠI',
      title: mode === 'service' ? 'Ngày & Ca khám' : 'Ngày & Giờ khám',
      status: 'current',
    },
    {
      stepNumber: 3,
      label: 'BƯỚC 3',
      title: 'Soát xét thông tin',
      status: 'upcoming',
    },
    {
      stepNumber: 4,
      label: 'BƯỚC 4',
      title: 'Hoàn tất & Cấp số STT',
      status: 'upcoming',
    },
  ];

  return (
    <section
      aria-label="Tiến trình đặt lịch khám"
      className="mb-5 rounded-2xl border border-border bg-white px-5 py-4 shadow-xs"
    >
      <ol className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {steps.map((step, index) => {
          const isCompleted = step.status === 'completed';
          const isCurrent = step.status === 'current';

          return (
            <li
              key={step.stepNumber}
              aria-current={isCurrent ? 'step' : undefined}
              className="relative flex items-center gap-3"
            >
              <div
                className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-bold transition-colors ${
                  isCompleted
                    ? 'bg-emerald-500 text-white'
                    : isCurrent
                      ? 'bg-primary text-white shadow-xs ring-4 ring-sky-50'
                      : 'border border-border bg-canvas text-sub'
                }`}
              >
                {isCompleted ? (
                  <Check className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" />
                ) : (
                  <span>{step.stepNumber}</span>
                )}
              </div>

              <div className="min-w-0 flex-1">
                <p
                  className={`text-[10px] font-bold uppercase tracking-wider ${
                    isCurrent ? 'text-primary' : isCompleted ? 'text-emerald-600' : 'text-sub'
                  }`}
                >
                  {step.label}
                </p>
                <p
                  className={`truncate text-xs font-semibold ${
                    isCurrent || isCompleted ? 'text-ink' : 'text-sub'
                  }`}
                >
                  {step.title}
                </p>
              </div>

              {index < steps.length - 1 && (
                <div
                  aria-hidden="true"
                  className={`hidden h-0.5 w-8 shrink-0 lg:block ${
                    isCompleted ? 'bg-primary' : 'bg-border'
                  }`}
                />
              )}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
