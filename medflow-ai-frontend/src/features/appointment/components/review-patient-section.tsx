import { CheckCircle2, UserRound } from 'lucide-react';

import type { BookingPatientProfile } from '../types/slot-booking.types';

interface ReviewPatientSectionProps {
  patient: BookingPatientProfile;
  reasonForVisit: string;
}

export function ReviewPatientSection({ patient, reasonForVisit }: ReviewPatientSectionProps) {
  const trimmedReason = reasonForVisit.trim();
  const ageAndBirthLabel = patient.birthDateLabel
    ? `${patient.birthDateLabel} (${patient.age} tuổi)`
    : `${patient.age} tuổi`;

  return (
    <section
      aria-label="Thông tin người bệnh"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <UserRound className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-sm font-bold text-ink uppercase">1. THÔNG TIN NGƯỜI BỆNH</h2>
            <p className="text-xs text-sub">
              Được trích xuất từ dữ liệu hồ sơ ngoại trú điện tử (EHR)
            </p>
          </div>
        </div>

        {patient.badgeVariant === 'primary' ? (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">
            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Chính chủ tài khoản</span>
          </span>
        ) : (
          <span className="inline-flex items-center rounded-full border border-slate-200 bg-slate-100 px-3 py-1 text-xs font-bold text-sub">
            {patient.badgeText}
          </span>
        )}
      </div>

      <div className="mt-5 grid gap-x-8 gap-y-4 sm:grid-cols-2">
        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
            HỌ VÀ TÊN NGƯỜI BỆNH
          </p>
          <p className="mt-1 text-lg font-bold text-ink">{patient.fullName}</p>
        </div>

        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
            MÃ ĐỊNH DANH NGƯỜI BỆNH (PID)
          </p>
          <span className="mt-1 inline-block rounded-md bg-sky-50 px-2 py-0.5 font-mono text-sm font-semibold text-primary">
            {patient.patientCode ?? patient.id}
          </span>
        </div>

        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
            NGÀY SINH &amp; TUỔI
          </p>
          <p className="mt-1 text-sm font-semibold text-ink">{ageAndBirthLabel}</p>
        </div>

        <div>
          <p className="text-[11px] font-bold tracking-wide text-sub uppercase">GIỚI TÍNH</p>
          <p className="mt-1 text-sm font-semibold text-ink">{patient.gender}</p>
        </div>

        {patient.phone && (
          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              SỐ ĐIỆN THOẠI LIÊN HỆ
            </p>
            <p className="mt-1 text-sm font-semibold text-ink">{patient.phone}</p>
          </div>
        )}

        {patient.nationalId && (
          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              SỐ THẺ CĂN CƯỚC / CCCD
            </p>
            <p className="mt-1 font-mono text-sm font-semibold text-ink">{patient.nationalId}</p>
          </div>
        )}

        {patient.address && (
          <div className="sm:col-span-2">
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              ĐỊA CHỈ CƯ TRÚ HIỆN TẠI
            </p>
            <p className="mt-1 text-sm font-semibold text-ink">{patient.address}</p>
          </div>
        )}
      </div>

      <div className="mt-5">
        <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
          LÝ DO KHÁM &amp; TRIỆU CHỨNG KHAI BÁO BAN ĐẦU
        </p>
        <div className="mt-1.5 rounded-xl border border-border bg-canvas px-4 py-3 text-sm">
          {trimmedReason ? (
            <p className="font-medium text-ink">{`"${trimmedReason}"`}</p>
          ) : (
            <p className="text-sub italic">Chưa khai báo lý do khám.</p>
          )}
        </div>
      </div>
    </section>
  );
}
