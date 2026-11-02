import { Check } from 'lucide-react';

interface BookingStepItem {
  stepNumber: number;
  label: string;
  title: string;
  status: 'completed' | 'current' | 'upcoming';
}

interface BookingStepperProps {
  mode?: 'doctor' | 'service';
  currentStep?: 2 | 3 | 4;
}

function resolveStepStatus(
  stepNumber: number,
  currentStep: 2 | 3 | 4,
): 'completed' | 'current' | 'upcoming' {
  if (currentStep === 4) {
    return 'completed';
  }
  if (stepNumber < currentStep) {
    return 'completed';
  }
  if (stepNumber === currentStep) {
    return 'current';
  }
  return 'upcoming';
}

function resolveStepLabel(stepNumber: number, currentStep: 2 | 3 | 4): string {
  if (currentStep === 4 && stepNumber === 4) {
    return 'BƯỚC 4 • HOÀN TẤT';
  }
  if (stepNumber === currentStep) {
    return `BƯỚC ${stepNumber} — HIỆN TẠI`;
  }
  return `BƯỚC ${stepNumber}`;
}

export function BookingStepper({ mode = 'doctor', currentStep = 2 }: BookingStepperProps) {
  const steps: BookingStepItem[] = [
    {
      stepNumber: 1,
      label: resolveStepLabel(1, currentStep),
      title: mode === 'service' ? 'Dịch vụ khám' : 'Khoa & Bác sĩ',
      status: resolveStepStatus(1, currentStep),
    },
    {
      stepNumber: 2,
      label: resolveStepLabel(2, currentStep),
      title: mode === 'service' ? 'Ngày & Ca khám' : 'Ngày & Giờ khám',
      status: resolveStepStatus(2, currentStep),
    },
    {
      stepNumber: 3,
      label: resolveStepLabel(3, currentStep),
      title: 'Soát xét thông tin',
      status: resolveStepStatus(3, currentStep),
    },
    {
      stepNumber: 4,
      label: resolveStepLabel(4, currentStep),
      title: 'Hoàn tất & Cấp số STT',
      status: resolveStepStatus(4, currentStep),
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
          const isFinalCompletedPill = currentStep === 4 && step.stepNumber === 4;

          return (
            <li
              key={step.stepNumber}
              aria-current={isCurrent ? 'step' : undefined}
              className={
                isFinalCompletedPill
                  ? 'relative flex items-center gap-3 rounded-xl bg-emerald-600 px-3 py-2 text-white'
                  : 'relative flex items-center gap-3'
              }
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
                    isFinalCompletedPill
                      ? 'text-white'
                      : isCurrent
                        ? 'text-primary'
                        : isCompleted
                          ? 'text-emerald-600'
                          : 'text-sub'
                  }`}
                >
                  {step.label}
                </p>
                <p
                  className={`truncate text-xs font-semibold ${
                    isFinalCompletedPill
                      ? 'text-white'
                      : isCurrent || isCompleted
                        ? 'text-ink'
                        : 'text-sub'
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
