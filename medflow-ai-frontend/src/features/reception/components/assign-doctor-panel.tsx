import { BadgeCheck, CheckCircle2 } from 'lucide-react';
import type { DoctorWorkloadItem, VisitPatientSummary } from '../types/assign-doctor.types';
import { DoctorWorkloadCard } from './doctor-workload-card';

interface AssignDoctorPanelProps {
  patient: VisitPatientSummary;
  doctors: DoctorWorkloadItem[];
  selectedDoctorId: string | null;
  onSelectDoctor: (doctorId: string) => void;
  onConfirm: () => void;
  isSubmitting?: boolean;
}

export function AssignDoctorPanel({
  patient,
  doctors,
  selectedDoctorId,
  onSelectDoctor,
  onConfirm,
  isSubmitting = false,
}: AssignDoctorPanelProps) {
  const selected = doctors.find((d) => d.id === selectedDoctorId) ?? null;

  return (
    <div className="space-y-6">
      <section
        aria-label="Thông tin bệnh nhân"
        className="rounded-2xl border border-border bg-white p-5 flex flex-wrap items-center justify-between gap-4"
      >
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-bold text-navy">{patient.fullName}</h2>
            <span className="text-sm text-sub">
              {patient.gender}, {patient.age} tuổi
            </span>
            {patient.cccdVerified && (
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 border border-emerald-200 px-2 py-0.5 text-xs font-semibold text-emerald-700">
                <BadgeCheck className="h-3.5 w-3.5" aria-hidden="true" />
                CCCD đã xác thực
              </span>
            )}
          </div>
          <div className="text-sm text-sub">Mã BN: {patient.patientCode}</div>
        </div>
        <div className="text-right space-y-1">
          <div className="text-sm text-ink">
            Dịch vụ: {patient.serviceName} ({patient.serviceCode})
          </div>
          <div className="text-sm font-bold text-navy">
            {patient.price.toLocaleString('vi-VN')} VNĐ
          </div>
        </div>
      </section>

      <section aria-label="Chọn bác sĩ" className="space-y-3">
        <h3 className="text-sm font-bold text-navy">Chọn bác sĩ theo tải công việc</h3>
        <div role="radiogroup" aria-label="Danh sách bác sĩ" className="space-y-3">
          {doctors.map((doctor) => (
            <DoctorWorkloadCard
              key={doctor.id}
              doctor={doctor}
              selected={doctor.id === selectedDoctorId}
              onSelect={onSelectDoctor}
            />
          ))}
        </div>
      </section>

      <div className="rounded-2xl border border-border bg-white p-4 flex flex-wrap items-center justify-between gap-4">
        <div className="text-sm text-sub" data-testid="selected-summary">
          {selected ? (
            <>
              Đã chọn: <strong className="text-navy">{selected.fullName}</strong> —{' '}
              {selected.roomName}
            </>
          ) : (
            'Chưa chọn bác sĩ'
          )}
        </div>
        <button
          type="button"
          aria-label="Xác nhận phân bổ bác sĩ"
          disabled={!selected || isSubmitting}
          onClick={onConfirm}
          className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-dark disabled:cursor-not-allowed disabled:opacity-50"
        >
          <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
          Xác nhận phân bổ bác sĩ
        </button>
      </div>
    </div>
  );
}
