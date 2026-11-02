import { Building2, Check, MapPin } from 'lucide-react';

import type { Hospital } from '../types/booking-entry.types';

interface BookingHospitalCardProps {
  hospital: Hospital;
  selected: boolean;
  onSelect: (id: string) => void;
}

export function BookingHospitalCard({ hospital, selected, onSelect }: BookingHospitalCardProps) {
  const stateClasses = !hospital.isActive
    ? 'cursor-not-allowed border-border bg-slate-50 opacity-60'
    : selected
      ? 'border-primary bg-sky-50/60 ring-2 ring-sky-100'
      : 'border-border bg-white hover:border-sky-300';

  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      disabled={!hospital.isActive}
      onClick={() => onSelect(hospital.id)}
      className={`rounded-2xl border p-4 text-left transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${stateClasses}`}
    >
      <div className="flex items-start gap-3">
        <span
          className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${
            selected ? 'bg-primary text-white' : 'bg-sky-50 text-primary'
          }`}
        >
          <Building2 className="h-5 w-5" aria-hidden="true" />
        </span>

        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <p className="text-sm font-bold leading-snug text-ink">{hospital.name}</p>
            <span
              className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 ${
                selected ? 'border-primary bg-primary' : 'border-slate-300 bg-white'
              }`}
            >
              {selected && <Check className="h-3 w-3 text-white" aria-hidden="true" />}
            </span>
          </div>

          <p className="mt-1 flex items-start gap-1 text-xs text-sub">
            <MapPin className="mt-0.5 h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="leading-snug">{hospital.address}</span>
          </p>

          <div className="mt-2 flex flex-wrap items-center gap-2">
            <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-slate-600">
              {hospital.hospitalCode}
            </span>
            {hospital.isActive ? (
              <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-emerald-600">
                <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" aria-hidden="true" />
                Đang tiếp nhận
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-slate-500">
                <span className="h-1.5 w-1.5 rounded-full bg-slate-400" aria-hidden="true" />
                Tạm ngưng tiếp nhận
              </span>
            )}
          </div>
        </div>
      </div>
    </button>
  );
}
