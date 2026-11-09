import {
  Activity,
  Building2,
  Calendar,
  HeartPulse,
  ShieldAlert,
  ShieldCheck,
  Stethoscope,
  UserRound,
} from 'lucide-react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent } from '@/shared/ui/card';

import type {
  MedicalHistoryCurrentVisitDto,
  MedicalHistoryPatientSummaryDto,
} from '../types/medical-history.types';
import { getGenderLabel, getVisitStatusMeta } from '../utils/medical-history-meta';

export interface MedicalHistoryPatientBannerProps {
  patient: MedicalHistoryPatientSummaryDto;
  currentVisit: MedicalHistoryCurrentVisitDto | null;
  facilityName: string;
  generatedAtUtc: string;
}

function formatDateTimeVi(value: string | null | undefined): string {
  if (!value) return 'Chưa ghi nhận';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function MedicalHistoryPatientBanner({
  patient,
  currentVisit,
  facilityName,
  generatedAtUtc,
}: MedicalHistoryPatientBannerProps) {
  const hasKnownAllergy =
    Boolean(patient.allergiesSummary?.trim()) &&
    patient.allergiesSummary.trim() !== 'Chưa ghi nhận tiền sử dị ứng';

  const currentVisitStatusMeta = currentVisit ? getVisitStatusMeta(currentVisit.status) : null;

  return (
    <Card>
      <CardContent className="p-5 space-y-4">
        <div className="flex flex-col lg:flex-row lg:items-start lg:justify-between gap-4">
          <div className="flex items-start gap-3.5 min-w-0">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-sky-50 border border-sky-200 text-primary">
              <UserRound className="h-6 w-6" aria-hidden="true" />
            </div>
            <div className="min-w-0 space-y-1.5">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="text-lg font-bold text-navy">{patient.fullName}</h2>
                <span className="inline-flex items-center rounded-md bg-canvas border border-border px-2 py-0.5 text-xs font-mono font-semibold text-navy">
                  {patient.patientCode}
                </span>
                <Badge tone="slate">{getGenderLabel(patient.gender)}</Badge>
                <Badge tone="sky">{patient.age} tuổi</Badge>
                {patient.bloodType && (
                  <Badge tone="rose" className="font-mono">
                    Nhóm máu: {patient.bloodType}
                  </Badge>
                )}
              </div>

              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
                {patient.dateOfBirth && (
                  <span className="inline-flex items-center gap-1.5">
                    <Calendar className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
                    Ngày sinh: <strong className="font-mono text-ink">{patient.dateOfBirth}</strong>
                  </span>
                )}
                <span className="inline-flex items-center gap-1.5">
                  <Building2 className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
                  Cơ sở y tế: <strong className="text-ink">{facilityName}</strong>
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <Activity className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
                  Trích xuất lúc:{' '}
                  <strong className="font-mono text-ink">{formatDateTimeVi(generatedAtUtc)}</strong>
                </span>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2 shrink-0">
            <span className="inline-flex items-center gap-1.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-1.5 text-[11px] font-semibold text-emerald-800">
              <ShieldCheck className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
              Dữ liệu bệnh án đã ẩn thông tin liên hệ cá nhân và được ghi nhật ký truy cập
              (MEDICAL_HISTORY_VIEWED).
            </span>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-3 pt-1">
          {hasKnownAllergy ? (
            <Alert tone="warning" className="border-rose-200 bg-rose-50 text-rose-800">
              <div className="flex items-start gap-2">
                <ShieldAlert className="h-4 w-4 shrink-0 text-rose-600 mt-0.5" aria-hidden="true" />
                <div>
                  <div className="font-bold text-rose-800">Cảnh báo tiền sử dị ứng</div>
                  <div className="text-xs text-rose-700 mt-0.5">{patient.allergiesSummary}</div>
                </div>
              </div>
            </Alert>
          ) : (
            <div className="flex items-center gap-2.5 rounded-lg border border-border bg-canvas px-3.5 py-3 text-xs text-sub">
              <ShieldCheck className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
              <div>
                <span className="font-semibold text-navy">Tiền sử dị ứng: </span>
                <span>{patient.allergiesSummary || 'Chưa ghi nhận tiền sử dị ứng'}</span>
              </div>
            </div>
          )}

          <div className="flex flex-col justify-center rounded-lg border border-border bg-canvas px-3.5 py-3 text-xs">
            <div className="flex items-center gap-1.5 font-semibold text-navy mb-1.5">
              <HeartPulse className="h-4 w-4 text-amber-600" aria-hidden="true" />
              <span>Bệnh nền / Tình trạng mạn tính</span>
            </div>
            {patient.chronicConditions.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {patient.chronicConditions.map((condition) => (
                  <Badge key={condition} tone="amber">
                    {condition}
                  </Badge>
                ))}
              </div>
            ) : (
              <span className="text-sub">Chưa ghi nhận bệnh lý mạn tính đi kèm.</span>
            )}
          </div>
        </div>

        {currentVisit && currentVisitStatusMeta && (
          <div className="rounded-xl border border-sky-200 bg-sky-50/70 px-4 py-3 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
            <div className="space-y-1 min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 text-xs font-bold text-navy">
                  <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
                  Ngữ cảnh lượt khám hiện tại:
                </span>
                <span className="font-mono text-xs font-bold text-primary">
                  {currentVisit.visitCode}
                </span>
                <Badge tone={currentVisitStatusMeta.tone}>{currentVisitStatusMeta.label}</Badge>
                {currentVisit.roomCode && (
                  <span className="font-mono text-xs font-semibold text-navy">
                    • {currentVisit.roomCode}
                  </span>
                )}
                {currentVisit.departmentName && (
                  <span className="text-xs text-sub">({currentVisit.departmentName})</span>
                )}
              </div>
              {currentVisit.reasonForVisit && (
                <p className="text-xs text-ink">
                  <span className="font-semibold text-navy">Lý do khám: </span>
                  {currentVisit.reasonForVisit}
                </p>
              )}
            </div>
            {currentVisit.doctorName && (
              <div className="text-xs text-sub shrink-0">
                BS phụ trách: <strong className="text-navy">{currentVisit.doctorName}</strong>
              </div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
