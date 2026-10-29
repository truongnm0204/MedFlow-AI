import { UserCheck, UserPlus } from 'lucide-react';
import { Link } from 'react-router-dom';

import type {
  BookingPatientProfile,
  PatientProfileBadgeVariant,
} from '../types/slot-booking.types';

interface BookingPatientSelectorProps {
  profiles: BookingPatientProfile[];
  selectedProfileId: string;
  onSelectProfile: (profileId: string) => void;
}

function getProfileBadgeClass(variant: PatientProfileBadgeVariant): string {
  switch (variant) {
    case 'primary':
      return 'bg-sky-100/80 text-primary';
    case 'pediatric':
      return 'bg-slate-100 text-sub';
    case 'geriatric':
      return 'bg-slate-100 text-sub';
  }
}

export function BookingPatientSelector({
  profiles,
  selectedProfileId,
  onSelectProfile,
}: BookingPatientSelectorProps) {
  return (
    <section
      aria-labelledby="section-patient-heading"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="mb-4 flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
        <div className="flex items-start gap-3">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
            <UserCheck className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 id="section-patient-heading" className="text-sm font-bold text-ink sm:text-base">
              1. Chọn Người Thăm Khám
            </h2>
            <p className="text-xs text-sub">Thông tin được gắn với mã số hồ sơ điện tử EMR</p>
          </div>
        </div>

        <Link
          to="/patient/family"
          className="inline-flex items-center gap-1.5 text-xs font-semibold text-primary transition-colors hover:text-primary-dark"
        >
          <UserPlus className="h-3.5 w-3.5" aria-hidden="true" />
          <span>+ Thêm hồ sơ người thân (SCR-PRO-05)</span>
        </Link>
      </div>

      <div
        role="radiogroup"
        aria-label="Hồ sơ người thăm khám"
        className="grid grid-cols-1 gap-3.5 md:grid-cols-3"
      >
        {profiles.map((profile) => {
          const isSelected = profile.id === selectedProfileId;

          return (
            <button
              key={profile.id}
              type="button"
              role="radio"
              aria-checked={isSelected}
              onClick={() => onSelectProfile(profile.id)}
              className={`flex flex-col justify-between rounded-xl border p-4 text-left transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                isSelected
                  ? 'border-2 border-primary bg-sky-50/40 shadow-xs'
                  : 'border-border bg-white hover:border-sky-300 hover:bg-canvas/50'
              }`}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-start gap-2.5">
                  <span
                    aria-hidden="true"
                    className={`mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full border ${
                      isSelected ? 'border-primary bg-white' : 'border-slate-300 bg-white'
                    }`}
                  >
                    {isSelected && <span className="h-2 w-2 rounded-full bg-primary" />}
                  </span>

                  <div>
                    <div className="text-xs font-bold text-ink sm:text-sm">
                      {profile.fullName}{' '}
                      <span className="font-normal text-sub">{profile.relationshipLabel}</span>
                    </div>
                    <p className="mt-0.5 text-xs text-sub">
                      {profile.gender} • {profile.age} tuổi
                    </p>
                  </div>
                </div>

                <span
                  className={`shrink-0 rounded-md px-2 py-0.5 text-[10px] font-semibold ${getProfileBadgeClass(
                    profile.badgeVariant,
                  )}`}
                >
                  {profile.badgeText}
                </span>
              </div>

              <div className="mt-3 border-t border-border/80 pt-2.5 text-[11px] text-sub space-y-0.5">
                <p>{profile.metaPrimary}</p>
                <p
                  className={
                    profile.metaSecondary.startsWith('PID:') ? 'font-mono text-[11px]' : ''
                  }
                >
                  {profile.metaSecondary}
                </p>
              </div>
            </button>
          );
        })}
      </div>
    </section>
  );
}
