import { useState, type ReactNode } from 'react';
import {
  CalendarClock,
  IdCard,
  MapPin,
  Phone,
  ShieldAlert,
  ShieldCheck,
  UserRound,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import type { FinalizeEkycSessionResponse } from '@/features/ekyc/types/ekyc.types';

import { EkycAssistedModal } from './ekyc-assisted-modal';
import type {
  PatientDetail,
  PatientGender,
  TodayAppointmentStatus,
} from '../types/patient-detail.types';
import {
  PATIENT_IDENTITY_STATUS_LABELS,
  PATIENT_UNVERIFIED_REASON_LABELS,
} from '../types/patient-identity.types';

interface PatientDetailModalProps {
  patient: PatientDetail | null;
  onClose: () => void;
  onActivate?: (patient: PatientDetail) => void;
  /** Trang cha tự mở luồng eKYC (dùng khi id hồ sơ không phải số). */
  onStartEkyc?: (patient: PatientDetail) => void;
  /** Được gọi khi hồ sơ vừa được xác minh xong để trang cha tải lại dữ liệu. */
  onIdentityVerified?: (result: FinalizeEkycSessionResponse) => void;
}

const GENDER_LABEL: Record<PatientGender, string> = {
  MALE: 'Nam',
  FEMALE: 'Nữ',
  OTHER: 'Khác',
};

const APPOINTMENT_STATUS: Record<
  TodayAppointmentStatus,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' }
> = {
  BOOKED: { label: 'ĐÃ ĐẶT LỊCH', tone: 'sky' },
  ARRIVED: { label: 'ĐÃ CÓ MẶT TẠI SẢNH', tone: 'emerald' },
  IN_PROGRESS: { label: 'ĐANG KHÁM', tone: 'amber' },
  DONE: { label: 'ĐÃ HOÀN TẤT', tone: 'slate' },
};

function getInitials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';
  return `${first}${last}`.toUpperCase();
}

function InfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-[10px] font-bold uppercase tracking-wider text-sub">{label}</dt>
      <dd className="mt-0.5 text-sm font-semibold text-navy">{children}</dd>
    </div>
  );
}

export function PatientDetailModal({
  patient,
  onClose,
  onActivate,
  onStartEkyc,
  onIdentityVerified,
}: PatientDetailModalProps) {
  const [ekycOpen, setEkycOpen] = useState(false);

  const identityStatus = patient?.identityStatus ?? 'UNVERIFIED';
  const numericPatientId = patient ? Number(patient.id) : Number.NaN;
  const canOpenEkycLocally = Number.isInteger(numericPatientId) && numericPatientId > 0;
  const showEkycAction =
    patient !== null &&
    identityStatus === 'UNVERIFIED' &&
    (onStartEkyc !== undefined || canOpenEkycLocally);

  const handleStartEkyc = () => {
    if (!patient) return;
    if (onStartEkyc) {
      onStartEkyc(patient);
      return;
    }
    setEkycOpen(true);
  };

  const handleCloseDetail = () => {
    setEkycOpen(false);
    onClose();
  };

  return (
    <Modal open={patient !== null} onClose={handleCloseDetail} maxWidth="max-w-2xl">
      {patient && (
        <div className="max-h-[90vh] overflow-y-auto" aria-label="Chi tiết bệnh nhân">
          {/* Header */}
          <div className="flex items-center gap-4 border-b border-border p-6 pr-14">
            <div
              className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-primary text-lg font-bold text-white"
              aria-hidden="true"
            >
              {getInitials(patient.fullName)}
            </div>
            <div className="min-w-0">
              <h2 className="truncate text-lg font-bold text-navy">
                {patient.fullName} • {GENDER_LABEL[patient.gender]}, {patient.age} tuổi
              </h2>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-sub">
                <span className="font-mono font-semibold">{patient.patientCode}</span>
                <Badge tone="sky">
                  <IdCard className="mr-1 h-3 w-3" aria-hidden="true" />
                  CCCD {patient.cccd}
                </Badge>
                <span className="inline-flex items-center gap-1">
                  <Phone className="h-3 w-3" aria-hidden="true" />
                  {patient.phone}
                </span>
              </div>
            </div>
          </div>

          <div className="space-y-6 p-6">
            {/* Today's appointment */}
            {patient.todayAppointment && (
              <section
                aria-label="Lịch hẹn hôm nay"
                className="flex flex-wrap items-center gap-2 rounded-xl border border-border bg-canvas px-4 py-3 text-sm text-navy"
              >
                <CalendarClock className="h-4 w-4 text-primary" aria-hidden="true" />
                <span className="font-semibold">{patient.todayAppointment.timeRange}</span>
                <span>• {patient.todayAppointment.doctorName}</span>
                <span>• {patient.todayAppointment.room}</span>
                <Badge tone={APPOINTMENT_STATUS[patient.todayAppointment.status].tone}>
                  {APPOINTMENT_STATUS[patient.todayAppointment.status].label}
                </Badge>
              </section>
            )}

            {/* Administrative info */}
            <section aria-label="Thông tin hành chính">
              <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-sub">
                Thông tin hành chính
              </h3>
              <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <InfoRow label="Ngày sinh">{patient.dateOfBirth}</InfoRow>
                <InfoRow label="Số BHYT">
                  {patient.bhytNumber ? (
                    <Badge tone="emerald">
                      <ShieldCheck className="mr-1 h-3 w-3" aria-hidden="true" />
                      {patient.bhytNumber}
                    </Badge>
                  ) : (
                    'Không có'
                  )}
                </InfoRow>
                <InfoRow label="Địa chỉ">
                  <span className="inline-flex items-start gap-1">
                    <MapPin className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    {patient.address}
                  </span>
                </InfoRow>
                <InfoRow label="Người liên hệ khẩn cấp">
                  {patient.emergencyContact ? (
                    <span className="inline-flex items-start gap-1">
                      <UserRound className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      {patient.emergencyContact.name} ({patient.emergencyContact.relationship}) •{' '}
                      {patient.emergencyContact.phone}
                    </span>
                  ) : (
                    'Chưa cập nhật'
                  )}
                </InfoRow>
              </dl>
            </section>

            {/* Identity verification */}
            <section aria-label="Trạng thái xác minh danh tính">
              <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-sub">
                Xác minh danh tính
              </h3>
              <div className="space-y-3 rounded-xl border border-border bg-canvas px-4 py-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={identityStatus === 'VERIFIED' ? 'emerald' : 'slate'}>
                    {identityStatus === 'VERIFIED' ? (
                      <ShieldCheck className="mr-1 h-3 w-3" aria-hidden="true" />
                    ) : (
                      <ShieldAlert className="mr-1 h-3 w-3" aria-hidden="true" />
                    )}
                    {PATIENT_IDENTITY_STATUS_LABELS[identityStatus]}
                  </Badge>
                  {identityStatus === 'UNVERIFIED' && patient.unverifiedReason ? (
                    <span className="text-xs text-sub">
                      Lý do: {PATIENT_UNVERIFIED_REASON_LABELS[patient.unverifiedReason]}
                    </span>
                  ) : null}
                </div>
                {patient.identityNote ? (
                  <p className="text-xs text-sub">Ghi chú: {patient.identityNote}</p>
                ) : null}
                {showEkycAction ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    onClick={handleStartEkyc}
                    aria-label="Xác minh eKYC hỗ trợ cho bệnh nhân"
                  >
                    <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                    Xác minh eKYC hỗ trợ
                  </Button>
                ) : null}
              </div>
            </section>

            {/* Past visits */}
            <section aria-label="Lịch sử khám">
              <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-sub">
                Lịch sử khám
              </h3>
              {patient.pastVisits.length === 0 ? (
                <p className="text-sm text-sub">Chưa có lượt khám trước đó.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="border-b border-border text-left text-[10px] font-bold uppercase tracking-wider text-sub">
                        <th className="py-2 pr-3">Ngày khám</th>
                        <th className="py-2 pr-3">Khoa</th>
                        <th className="py-2 pr-3">Bác sĩ</th>
                        <th className="py-2">Chẩn đoán</th>
                      </tr>
                    </thead>
                    <tbody>
                      {patient.pastVisits.map((v) => (
                        <tr key={v.id} className="border-b border-border last:border-0">
                          <td className="py-2.5 pr-3 tabular-nums text-navy">{v.date}</td>
                          <td className="py-2.5 pr-3 text-navy">{v.department}</td>
                          <td className="py-2.5 pr-3 text-navy">{v.doctorName}</td>
                          <td className="py-2.5 text-sub">{v.diagnosis}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          </div>

          {/* Actions */}
          <div className="flex justify-end gap-3 border-t border-border p-4">
            <Button
              variant="secondary"
              size="md"
              onClick={handleCloseDetail}
              aria-label="Đóng hộp thoại"
            >
              Đóng
            </Button>
            <Button
              variant="primary"
              size="md"
              onClick={() => onActivate?.(patient)}
              aria-label="Tiếp nhận / Kích hoạt lượt khám"
            >
              Tiếp nhận / Kích hoạt lượt khám
            </Button>
          </div>

          {ekycOpen && canOpenEkycLocally ? (
            <EkycAssistedModal
              open
              patientId={numericPatientId}
              patientName={patient.fullName}
              onClose={() => setEkycOpen(false)}
              onVerified={(result) => onIdentityVerified?.(result)}
            />
          ) : null}
        </div>
      )}
    </Modal>
  );
}
