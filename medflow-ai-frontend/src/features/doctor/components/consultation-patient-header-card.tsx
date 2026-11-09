import {
  Building2,
  Calendar,
  Droplets,
  Lock,
  MapPin,
  Phone,
  Play,
  ShieldAlert,
  Stethoscope,
  UserRound,
} from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { openConsultationWorkspaceSchema } from '../schemas/consultation-workspace.schema';
import type {
  ConsultationPatientSummaryDto,
  ConsultationQueueContextDto,
  ConsultationVisitSummaryDto,
  ConsultationWorkspaceActionDto,
  ConsultationWorkspaceState,
} from '../types/consultation-workspace.types';

export interface ConsultationPatientHeaderCardProps {
  workspaceState: ConsultationWorkspaceState | (string & {});
  actionOutcome: string;
  canEditClinical: boolean;
  readOnlyReason: string | null;
  visit: ConsultationVisitSummaryDto;
  patient: ConsultationPatientSummaryDto;
  queue: ConsultationQueueContextDto | null;
  openAction: ConsultationWorkspaceActionDto | undefined;
  isOpening: boolean;
  onOpenSession: (notes?: string) => void;
}

const WORKSPACE_STATE_BADGE: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate' }
> = {
  IN_PROGRESS: { label: 'Đang khám lâm sàng', tone: 'emerald' },
  CALLED: { label: 'Đã gọi vào phòng', tone: 'sky' },
  WAITING: { label: 'Đang chờ gọi', tone: 'amber' },
  RETURN_CONSULTATION: { label: 'Chờ khám lại đọc kết quả', tone: 'emerald' },
  AWAITING_RESULTS: { label: 'Chờ kết quả CLS', tone: 'rose' },
  COMPLETED: { label: 'Đã hoàn tất khám', tone: 'slate' },
};

export function ConsultationPatientHeaderCard({
  workspaceState,
  actionOutcome,
  canEditClinical,
  readOnlyReason,
  visit,
  patient,
  queue,
  openAction,
  isOpening,
  onOpenSession,
}: ConsultationPatientHeaderCardProps) {
  const [notes, setNotes] = useState('');
  const [notesError, setNotesError] = useState<string | null>(null);

  const stateBadge = WORKSPACE_STATE_BADGE[workspaceState] ?? {
    label: visit.statusLabel || workspaceState,
    tone: 'slate' as const,
  };

  const canOpenNow = Boolean(openAction?.enabled) || workspaceState === 'CALLED';
  const hasSignificantAllergy =
    Boolean(patient.allergiesSummary) &&
    !patient.allergiesSummary?.toLowerCase().includes('không ghi nhận');

  const handleOpenSubmit = (e: FormEvent) => {
    e.preventDefault();
    setNotesError(null);

    const parsed = openConsultationWorkspaceSchema.safeParse({
      expectedStateVersion: queue?.stateVersion ?? undefined,
      notes,
    });

    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setNotesError(firstIssue?.message ?? 'Thông tin mở phiên khám không hợp lệ.');
      return;
    }

    onOpenSession(parsed.data.notes ?? undefined);
  };

  return (
    <Card>
      <CardHeader className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
        <div className="flex items-start gap-3.5 min-w-0">
          <div className="h-12 w-12 shrink-0 rounded-2xl bg-primary/10 border border-primary/20 text-primary flex items-center justify-center">
            <UserRound className="h-6 w-6" aria-hidden="true" />
          </div>
          <div className="min-w-0 space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-base font-bold text-navy truncate">{patient.fullName}</h2>
              <span className="rounded-md bg-canvas border border-border px-2 py-0.5 font-mono text-xs font-bold text-navy">
                {patient.patientCode}
              </span>
              <Badge tone={stateBadge.tone}>{stateBadge.label}</Badge>
              <Badge tone={visit.visitType === 'SCHEDULED' ? 'sky' : 'slate'}>
                {visit.visitTypeLabel}
              </Badge>
              {queue && (
                <span className="rounded-md bg-emerald-50 border border-emerald-200 px-2 py-0.5 font-mono text-xs font-extrabold text-emerald-800">
                  Phiếu {queue.ticketNo} • TT #{queue.servingOrder}
                </span>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
              <span>
                Giới tính: <strong className="text-ink">{patient.genderLabel}</strong>
              </span>
              <span>
                Tuổi / Năm sinh:{' '}
                <strong className="font-mono text-ink">
                  {patient.age !== null ? `${patient.age} tuổi` : '—'}
                  {patient.dateOfBirth ? ` (${patient.dateOfBirth})` : ''}
                </strong>
              </span>
              {patient.bloodType && (
                <span className="inline-flex items-center gap-1">
                  <Droplets className="h-3.5 w-3.5 text-rose-500" aria-hidden="true" />
                  Nhóm máu: <strong className="font-mono text-ink">{patient.bloodType}</strong>
                </span>
              )}
              {patient.phone && (
                <span className="inline-flex items-center gap-1">
                  <Phone className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
                  <span className="font-mono text-ink">{patient.phone}</span>
                </span>
              )}
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2.5 bg-canvas rounded-xl px-3.5 py-2.5 border border-border">
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">Mã lượt khám</div>
            <div className="font-mono text-xs font-extrabold text-navy">{visit.visitCode}</div>
          </div>
          <div className="h-7 w-px bg-border" />
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">Buồng khám</div>
            <div className="text-xs font-bold text-navy">
              <span className="font-mono">{visit.roomCode ?? 'Chưa gán'}</span>
              {visit.roomName ? ` • ${visit.roomName}` : ''}
            </div>
          </div>
          <div className="h-7 w-px bg-border" />
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">Bác sĩ phụ trách</div>
            <div className="text-xs font-bold text-navy">{visit.assignedDoctorName}</div>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-xs">
          <div className="rounded-xl bg-canvas border border-border p-3 space-y-1">
            <div className="text-[11px] font-semibold text-sub flex items-center gap-1.5">
              <Stethoscope className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Dịch vụ & Lý do khám</span>
            </div>
            <div className="font-semibold text-navy">
              {visit.serviceName ?? 'Khám chuyên khoa ngoại trú'}
              {visit.serviceCode && (
                <span className="ml-1.5 font-mono text-[11px] text-sub">({visit.serviceCode})</span>
              )}
            </div>
            <div className="text-sub">{visit.reasonForVisit ?? 'Chưa ghi nhận lý do khám'}</div>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3 space-y-1">
            <div className="text-[11px] font-semibold text-sub flex items-center gap-1.5">
              <Building2 className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Khoa / Chuyên khoa & Địa chỉ BN</span>
            </div>
            <div className="font-semibold text-navy">
              {visit.departmentName} • {visit.specialtyName}
            </div>
            <div className="text-sub flex items-center gap-1 truncate">
              <MapPin className="h-3.5 w-3.5 shrink-0 text-sub" aria-hidden="true" />
              <span className="truncate">{patient.address ?? 'Chưa cập nhật địa chỉ'}</span>
            </div>
          </div>

          <div
            className={`rounded-xl border p-3 space-y-1 ${
              hasSignificantAllergy ? 'bg-rose-50/70 border-rose-200' : 'bg-canvas border-border'
            }`}
          >
            <div className="text-[11px] font-semibold flex items-center gap-1.5 text-rose-700">
              <ShieldAlert className="h-3.5 w-3.5 text-rose-600" aria-hidden="true" />
              <span>Cảnh báo Dị ứng & Tiền sử lưu ý</span>
            </div>
            <div
              className={`font-semibold ${hasSignificantAllergy ? 'text-rose-800' : 'text-ink'}`}
            >
              {patient.allergiesSummary ?? 'Chưa ghi nhận tiền sử dị ứng'}
            </div>
            {visit.appointmentCode && (
              <div className="text-[11px] text-sub flex items-center gap-1">
                <Calendar className="h-3 w-3" aria-hidden="true" />
                <span>Mã hẹn:</span>
                <span className="font-mono font-semibold text-navy">{visit.appointmentCode}</span>
              </div>
            )}
          </div>
        </div>

        {(actionOutcome === 'OPENED' || actionOutcome === 'ALREADY_IN_PROGRESS') && (
          <div role="status">
            <Alert tone="success">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>
                  {actionOutcome === 'OPENED'
                    ? `Đã mở phiên khám lâm sàng cho bệnh nhân ${patient.fullName} (${visit.visitCode}) và khởi tạo hồ sơ bệnh án nháp.`
                    : `Ca khám ${visit.visitCode} đang ở trạng thái Đang khám lâm sàng (IN_PROGRESS).`}
                </span>
                <Badge tone="emerald">
                  {actionOutcome === 'OPENED' ? 'Vừa bắt đầu khám' : 'Đang diễn ra'}
                </Badge>
              </div>
            </Alert>
          </div>
        )}

        {workspaceState === 'CALLED' && (
          <form
            onSubmit={handleOpenSubmit}
            aria-label="Biểu mẫu bắt đầu khám lâm sàng"
            className="rounded-xl border border-sky-200 bg-sky-50/70 p-4 space-y-3"
          >
            <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3">
              <div className="space-y-0.5">
                <div className="text-xs font-bold text-navy flex items-center gap-2">
                  <span>
                    Bệnh nhân đã được gọi vào phòng ({queue?.ticketNo ?? visit.visitCode})
                  </span>
                  {queue && (
                    <span className="font-mono text-[11px] font-semibold text-sub">
                      state_version: v{queue.stateVersion}
                    </span>
                  )}
                </div>
                <p className="text-xs text-sub">
                  Bấm <strong>Bắt đầu khám lâm sàng</strong> để chuyển lượt khám sang{' '}
                  <span className="font-mono font-semibold">IN_PROGRESS</span> và tạo hồ sơ bệnh án
                  nháp (<span className="font-mono">DRAFT</span>).
                </p>
              </div>

              <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2.5">
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => {
                    setNotes(e.target.value);
                    if (notesError) setNotesError(null);
                  }}
                  placeholder="Ghi chú mở phiên khám (tùy chọn, tối đa 300 ký tự)..."
                  aria-label="Ghi chú mở phiên khám"
                  className="w-full sm:w-72 rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
                <Button
                  type="submit"
                  variant="primary"
                  size="sm"
                  loading={isOpening}
                  disabled={!canOpenNow || isOpening}
                >
                  <Play className="h-3.5 w-3.5" aria-hidden="true" />
                  Bắt đầu khám lâm sàng
                </Button>
              </div>
            </div>

            {notesError && (
              <div role="alert" className="text-xs font-semibold text-rose-600">
                {notesError}
              </div>
            )}
          </form>
        )}

        {!canEditClinical && readOnlyReason && (
          <div className="rounded-xl border border-amber-200 bg-amber-50/80 px-3.5 py-2.5 text-xs text-amber-900 flex items-center gap-2">
            <Lock className="h-4 w-4 shrink-0 text-amber-700" aria-hidden="true" />
            <span>
              <strong>Chế độ chỉ đọc:</strong> {readOnlyReason}
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
