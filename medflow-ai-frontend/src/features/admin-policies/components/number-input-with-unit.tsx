import type { UseFormRegisterReturn } from 'react-hook-form';

import { formControlClass } from '@/shared/components/form/form-primitives';

export interface NumberInputWithUnitProps {
  id: string;
  unit: string;
  min: number;
  max: number;
  disabled?: boolean;
  registration: UseFormRegisterReturn;
}

export function NumberInputWithUnit({
  id,
  unit,
  min,
  max,
  disabled,
  registration,
}: NumberInputWithUnitProps) {
  return (
    <div className="relative mt-1.5 flex items-center">
      <input
        id={id}
        type="number"
        step={1}
        min={min}
        max={max}
        disabled={disabled}
        className={`${formControlClass} !mt-0 pr-16 font-mono font-semibold`}
        {...registration}
      />
      <span className="pointer-events-none absolute right-3 text-[11px] font-medium text-sub">
        {unit}
      </span>
    </div>
  );
}
