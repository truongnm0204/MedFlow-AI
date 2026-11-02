import { Check } from 'lucide-react';

type StepState = 'current' | 'done' | 'upcoming';

interface BookingEntryStepHeaderProps {
  step: 1 | 2;
  title: string;
  hint?: string;
  state: StepState;
}

const CIRCLE: Record<StepState, string> = {
  done: 'bg-emerald-500 text-white',
  current: 'bg-primary text-white ring-4 ring-sky-50',
  upcoming: 'border border-border bg-canvas text-sub',
};

const LABEL: Record<StepState, string> = {
  done: 'text-emerald-600',
  current: 'text-primary',
  upcoming: 'text-sub',
};

export function BookingEntryStepHeader({ step, title, hint, state }: BookingEntryStepHeaderProps) {
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="flex items-center gap-3">
        <span
          className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-bold ${CIRCLE[state]}`}
        >
          {state === 'done' ? <Check className="h-4 w-4" aria-hidden="true" /> : step}
        </span>
        <div>
          <p
            className={`text-[10px] font-bold uppercase tracking-wider ${LABEL[state]}`}
          >{`BƯỚC ${step}`}</p>
          <h2 className="text-base font-bold text-ink">{title}</h2>
        </div>
      </div>
      {hint && <p className="hidden pt-2 text-right text-xs text-sub sm:block">{hint}</p>}
    </div>
  );
}
