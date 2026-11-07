import { ArrowRightLeft, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { WalkInPatient } from '../types/walk-in-visit.types';

interface WalkInPatientBannerProps {
  patient: WalkInPatient;
  onChangePatient?: () => void;
}

const CURRENT_YEAR = new Date().getFullYear();

export function WalkInPatientBanner({ patient, onChangePatient }: WalkInPatientBannerProps) {
  const age = CURRENT_YEAR - patient.birthYear;

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        {/* Patient Demographics */}
        <div className="flex items-start sm:items-center gap-4">
          <div className="w-12 h-12 rounded-2xl bg-sky-100 border border-sky-200 text-sky-700 flex items-center justify-center font-bold text-sm shrink-0 shadow-2xs">
            {patient.fullName
              .split(' ')
              .map((n) => n[0])
              .slice(-2)
              .join('')
              .toUpperCase()}
          </div>

          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-base sm:text-lg font-bold text-slate-900 tracking-tight">
                {patient.fullName}
              </span>
              <span className="text-xs text-slate-500 font-medium">
                • {patient.gender === 'MALE' ? 'Nam' : 'Nữ'}, {age} tuổi ({patient.birthYear})
              </span>
              <Badge tone="emerald">
                <span className="flex items-center gap-1">
                  <ShieldCheck className="w-3.5 h-3.5" aria-hidden="true" />
                  CCCD đã xác thực
                </span>
              </Badge>
            </div>

            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-600 font-medium font-mono">
              <span>
                Mã BN: <strong className="text-sky-700">{patient.patientCode}</strong>
              </span>
              <span>CCCD: {patient.identityCard}</span>
              <span>SĐT: {patient.phoneNumber}</span>
            </div>

            <div className="text-[11px] text-amber-700 font-medium">● {patient.insuranceType}</div>
          </div>
        </div>

        {/* Change Patient Action */}
        <div className="shrink-0 self-end md:self-center">
          <Button variant="secondary" size="sm" onClick={onChangePatient} className="gap-1.5">
            <ArrowRightLeft className="w-3.5 h-3.5" aria-hidden="true" />
            <span>Thay đổi bệnh nhân / Tạo mới</span>
          </Button>
        </div>
      </div>
    </div>
  );
}
