import {
  Building2,
  Calendar,
  ChevronRight,
  FlaskConical,
  Pill,
  Stethoscope,
  UserRound,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import type { PatientMedicalRecordListItem } from '../types/patient-records.types';
import { formatEmrDateVn } from '../utils/emr-format';

export interface MedicalRecordCardProps {
  record: PatientMedicalRecordListItem;
}

export function MedicalRecordCard({ record }: MedicalRecordCardProps) {
  return (
    <article className="rounded-2xl border border-border bg-white p-5 shadow-xs transition-colors hover:border-sky-200">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs font-bold text-primary">{record.recordCode}</span>
            {record.visitCode && (
              <span className="rounded-md border border-border bg-canvas px-2 py-0.5 font-mono text-[11px] font-medium text-sub">
                {record.visitCode}
              </span>
            )}
            <span
              className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${
                record.isSelfProfile ? 'bg-sky-50 text-primary' : 'bg-indigo-50 text-indigo-700'
              }`}
            >
              {record.relationshipLabel}
            </span>
          </div>
          <h2 className="text-base font-bold text-navy">
            {record.primaryDiagnosisName ?? 'Khám lâm sàng ngoại trú đã hoàn tất'}
          </h2>
        </div>

        <div className="flex items-center gap-1.5 rounded-xl bg-canvas px-3 py-1.5 text-xs font-semibold text-navy">
          <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
          <span>{formatEmrDateVn(record.visitDate)}</span>
        </div>
      </div>

      <div className="mt-3 grid grid-cols-1 gap-2 text-xs text-sub sm:grid-cols-3">
        <div className="flex items-center gap-1.5">
          <UserRound className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
          <span className="truncate">
            BN: <strong className="font-semibold text-ink">{record.patientName}</strong>
            {record.patientCode && (
              <span className="ml-1 font-mono text-[11px] text-sub">({record.patientCode})</span>
            )}
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          <Building2 className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
          <span className="truncate">
            {record.hospitalName ?? 'Cơ sở MedFlow'}
            {record.specialtyName ? ` • ${record.specialtyName}` : ''}
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          <Stethoscope className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
          <span className="truncate">{record.doctorName ?? 'Bác sĩ điều trị'}</span>
        </div>
      </div>

      {(record.primaryIcd10Code || record.clinicalConclusion) && (
        <div className="mt-3.5 rounded-xl border border-border bg-canvas px-3.5 py-2.5 text-xs">
          {record.primaryIcd10Code && (
            <p className="font-semibold text-navy">
              ICD-10 chính:{' '}
              <span className="font-mono font-bold text-primary">{record.primaryIcd10Code}</span>
              {record.primaryDiagnosisName ? ` — ${record.primaryDiagnosisName}` : ''}
            </p>
          )}
          {record.clinicalConclusion && (
            <p className="mt-1 line-clamp-2 leading-relaxed text-sub">
              Kết luận: {record.clinicalConclusion}
            </p>
          )}
        </div>
      )}

      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-3.5">
        <div className="flex flex-wrap items-center gap-3 text-xs text-sub">
          <span className="inline-flex items-center gap-1">
            <FlaskConical className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>{record.diagnosticOrderCount} chỉ định CLS</span>
          </span>
          <span className="inline-flex items-center gap-1">
            <Pill className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
            <span>{record.prescriptionCount} đơn thuốc</span>
          </span>
        </div>

        <Link
          to={`/emr/${encodeURIComponent(record.recordCode)}`}
          className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
        >
          <span>Xem chi tiết bệnh án</span>
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </Link>
      </div>
    </article>
  );
}
