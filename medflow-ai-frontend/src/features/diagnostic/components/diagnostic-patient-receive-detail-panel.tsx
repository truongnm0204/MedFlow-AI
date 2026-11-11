import {
  AlertTriangle,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  Layers,
  Lock,
  ShieldAlert,
  Stethoscope,
  UserCheck,
  XCircle,
} from 'lucide-react';
import { useMemo, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import {
  RECEPTION_NOTE_PRESETS,
  receiveDiagnosticPatientSchema,
} from '../schemas/diagnostic-patient-receive.schema';
import type {
  DiagnosticClinicalPriority,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticPatientReceiveItemDto,
  DiagnosticPriority,
  DiagnosticServicePointOptionDto,
  ReceiveDiagnosticPatientRequest,
} from '../types/diagnostic.types';

interface DiagnosticPatientReceiveDetailPanelProps {
  order: DiagnosticPatientReceiveItemDto | null;
  clinicalContext?: DiagnosticOrderClinicalSummaryDto | null;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  hasReceivePermission: boolean;
  isSubmitting: boolean;
  onReceivePatient: (orderCode: string, payload: ReceiveDiagnosticPatientRequest) => void;
}

const RECEIVE_ELIGIBILITY_CRITERIA: Array<{
  key: keyof DiagnosticPatientReceiveItemDto['eligibility'];
  label: string;
}> = [
  {
    key: 'isOrderConfirmed',
    label: 'Phiếu chỉ định đã được kiểm tra & xác nhận hợp lệ (CONFIRMED — UC39)',
  },
  {
    key: 'isQueueWaitingForReception',
    label:
      'Hàng đợi cận lâm sàng đang chờ tiếp nhận tại điểm dịch vụ (WAITING / RESERVED / SKIPPED)',
  },
  {
    key: 'isVisitEligible',
    label: 'Lượt khám lâm sàng đang hoạt động hợp lệ',
  },
  {
    key: 'isPatientActive',
    label: 'Hồ sơ bệnh nhân đang hoạt động (ACTIVE)',
  },
  {
    key: 'isServiceActive',
    label: 'Danh mục dịch vụ cận lâm sàng đang mở',
  },
  {
    key: 'isDepartmentActive',
    label: 'Khoa/Phòng phụ trách chuyên môn đang hoạt động',
  },
  {
    key: 'isServicePointActive',
    label: 'Điểm phục vụ kỹ thuật (Service Point) sẵn sàng',
  },
  {
    key: 'isRoomActive',
    label: 'Phòng thực hiện cận lâm sàng đang mở cửa hoạt động',
  },
  {
    key: 'hasQueueManagePermission',
    label: 'Nhân viên có quyền điều phối & tiếp nhận hàng đợi (QUEUE_MANAGE)',
  },
];

function formatDateTime(iso?: string | null): string {
  if (!iso) return 'Chưa ghi nhận';
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('vi-VN', {
    style: 'currency',
    currency: 'VND',
    maximumFractionDigits: 0,
  }).format(amount);
}

interface DiagnosticPatientReceiveFormProps {
  order: DiagnosticPatientReceiveItemDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  canSubmitReceive: boolean;
  isSubmitting: boolean;
  onReceivePatient: (orderCode: string, payload: ReceiveDiagnosticPatientRequest) => void;
}

function DiagnosticPatientReceiveForm({
  order,
  availableServicePoints,
  canSubmitReceive,
  isSubmitting,
  onReceivePatient,
}: DiagnosticPatientReceiveFormProps) {
  const initialPriority = (order.priority?.toUpperCase() || 'NORMAL') as DiagnosticPriority;
  const initialManualPriority = (order.effectiveClinicalPriority?.toUpperCase() ||
    order.manualPriority?.toUpperCase() ||
    'NORMAL') as DiagnosticClinicalPriority;

  const [selectedServicePointId, setSelectedServicePointId] = useState<number | ''>(
    order.servicePointId ?? '',
  );
  const [priority, setPriority] = useState<DiagnosticPriority>(
    initialPriority === 'URGENT' || initialPriority === 'STAT' ? initialPriority : 'NORMAL',
  );
  const [manualPriority, setManualPriority] = useState<DiagnosticClinicalPriority>(
    initialManualPriority === 'ELDERLY' || initialManualPriority === 'PREGNANT'
      ? initialManualPriority
      : 'NORMAL',
  );
  const [instruction, setInstruction] = useState(order.instruction ?? '');
  const [receptionNote, setReceptionNote] = useState(order.receptionNote ?? '');
  const [isPatientPresent, setIsPatientPresent] = useState(order.presenceStatus !== 'ABSENT');
  const [isIdentityVerified, setIsIdentityVerified] = useState(true);
  const [isPreparationVerified, setIsPreparationVerified] = useState(true);
  const [validationError, setValidationError] = useState<string | null>(null);

  const compatibleServicePoints = useMemo(() => {
    const exactMatch = availableServicePoints.filter(
      (sp) => sp.isActive && sp.isRoomActive && (!sp.serviceId || sp.serviceId === order.serviceId),
    );
    return exactMatch.length > 0
      ? exactMatch
      : availableServicePoints.filter((sp) => sp.isActive && sp.isRoomActive);
  }, [availableServicePoints, order.serviceId]);

  const handleFormSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const chosenSp = availableServicePoints.find(
      (sp) => sp.servicePointId === Number(selectedServicePointId),
    );

    const candidatePayload = {
      isPatientPresent,
      isIdentityVerified,
      isPreparationVerified,
      servicePointId:
        selectedServicePointId !== '' ? Number(selectedServicePointId) : order.servicePointId,
      roomId: chosenSp?.roomId ?? order.roomId ?? null,
      priority,
      manualPriority,
      instruction: instruction.trim() ? instruction : null,
      receptionNote: receptionNote.trim() ? receptionNote : null,
      expectedStateVersion: order.stateVersion || 1,
    };

    const parsed = receiveDiagnosticPatientSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(
        firstIssue?.message ??
          'Dữ liệu tiếp nhận bệnh nhân cận lâm sàng không hợp lệ. Vui lòng kiểm tra lại.',
      );
      return;
    }

    onReceivePatient(order.orderCode, {
      isPatientPresent: parsed.data.isPatientPresent,
      patientPresent: parsed.data.isPatientPresent,
      isIdentityVerified: parsed.data.isIdentityVerified,
      identityVerified: parsed.data.isIdentityVerified,
      isPreparationVerified: parsed.data.isPreparationVerified,
      servicePointId: parsed.data.servicePointId ?? null,
      roomId: parsed.data.roomId ?? null,
      priority: parsed.data.priority ?? priority,
      manualPriority: parsed.data.manualPriority ?? manualPriority,
      clinicalPriority: parsed.data.manualPriority ?? manualPriority,
      instruction: parsed.data.instruction ?? null,
      receptionNote: parsed.data.receptionNote ?? null,
      expectedStateVersion: parsed.data.expectedStateVersion ?? order.stateVersion,
    });
  };

  return (
    <form
      onSubmit={handleFormSubmit}
      className="space-y-3 rounded-xl border border-border bg-canvas p-3.5"
      data-testid="receive-patient-form"
    >
      <div className="font-bold text-navy">
        Xác nhận bệnh nhân đến điểm dịch vụ & Sẵn sàng vào hàng đợi (UC40)
      </div>

      {validationError && (
        <Alert tone="error">
          <span data-testid="receive-form-validation-error">{validationError}</span>
        </Alert>
      )}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div>
          <label
            htmlFor="receive-service-point"
            className="block text-[11px] font-semibold text-navy"
          >
            Điểm phục vụ / Phòng kỹ thuật
          </label>
          <select
            id="receive-service-point"
            aria-label="Điểm phục vụ / Phòng kỹ thuật"
            value={selectedServicePointId}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) =>
              setSelectedServicePointId(e.target.value ? Number(e.target.value) : '')
            }
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
          >
            <option value="">Giữ mặc định ({order.roomCode ?? 'Tự động gán'})</option>
            {compatibleServicePoints.map((sp) => (
              <option key={sp.servicePointId} value={sp.servicePointId}>
                {sp.roomCode ? `${sp.roomCode} — ` : ''}
                {sp.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="receive-priority" className="block text-[11px] font-semibold text-navy">
            Mức ưu tiên chỉ định
          </label>
          <select
            id="receive-priority"
            aria-label="Mức ưu tiên chỉ định"
            value={priority}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) => setPriority(e.target.value as DiagnosticPriority)}
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
          >
            <option value="NORMAL">NORMAL — Bình thường</option>
            <option value="URGENT">URGENT — Khẩn</option>
            <option value="STAT">STAT — Cấp cứu tối khẩn</option>
          </select>
        </div>

        <div>
          <label
            htmlFor="receive-manual-priority"
            className="block text-[11px] font-semibold text-navy"
          >
            Ưu tiên lâm sàng (Hàng đợi)
          </label>
          <select
            id="receive-manual-priority"
            aria-label="Ưu tiên lâm sàng (Hàng đợi)"
            value={manualPriority}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) => setManualPriority(e.target.value as DiagnosticClinicalPriority)}
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
          >
            <option value="NORMAL">NORMAL — Tiêu chuẩn</option>
            <option value="ELDERLY">ELDERLY — Người cao tuổi (&ge;60T)</option>
            <option value="PREGNANT">PREGNANT — Thai phụ</option>
          </select>
        </div>
      </div>

      <div>
        <label htmlFor="receive-instruction" className="block text-[11px] font-semibold text-navy">
          Hướng dẫn kỹ thuật / Chuẩn bị tại phòng (tối đa 1000 ký tự)
        </label>
        <textarea
          id="receive-instruction"
          aria-label="Hướng dẫn kỹ thuật / Chuẩn bị tại phòng"
          rows={2}
          value={instruction}
          disabled={!canSubmitReceive || isSubmitting}
          onChange={(e) => setInstruction(e.target.value)}
          placeholder="Cập nhật hướng dẫn lấy mẫu, tư thế chụp hoặc lưu ý chuẩn bị..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label htmlFor="receive-note" className="block text-[11px] font-semibold text-navy">
          Ghi chú tiếp nhận bệnh nhân (tối đa 500 ký tự)
        </label>
        <textarea
          id="receive-note"
          aria-label="Ghi chú tiếp nhận bệnh nhân"
          rows={2}
          value={receptionNote}
          disabled={!canSubmitReceive || isSubmitting}
          onChange={(e) => setReceptionNote(e.target.value)}
          placeholder="Ghi chú xác nhận mặt bệnh nhân, đối chiếu danh tính, tình trạng chuẩn bị..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {RECEPTION_NOTE_PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              disabled={!canSubmitReceive || isSubmitting}
              onClick={() => setReceptionNote(preset)}
              className="rounded-md border border-border bg-white px-2 py-1 text-[10px] font-medium text-sub transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
            >
              + {preset.slice(0, 44)}...
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-2 pt-1">
        <label className="flex items-start gap-2 text-xs font-semibold text-navy">
          <input
            type="checkbox"
            aria-label="Bệnh nhân đã có mặt tại điểm dịch vụ cận lâm sàng"
            checked={isPatientPresent}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) => setIsPatientPresent(e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>Bệnh nhân đã có mặt tại điểm dịch vụ cận lâm sàng (PRESENT)</span>
        </label>

        <label className="flex items-start gap-2 text-xs font-semibold text-navy">
          <input
            type="checkbox"
            aria-label="Đã đối chiếu chính xác danh tính và thông tin hành chính bệnh nhân"
            checked={isIdentityVerified}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) => setIsIdentityVerified(e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>Đã đối chiếu chính xác danh tính và thông tin hành chính bệnh nhân</span>
        </label>

        <label className="flex items-start gap-2 text-xs text-ink">
          <input
            type="checkbox"
            aria-label="Bệnh nhân đủ điều kiện chuẩn bị thực hiện kỹ thuật"
            checked={isPreparationVerified}
            disabled={!canSubmitReceive || isSubmitting}
            onChange={(e) => setIsPreparationVerified(e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>
            Bệnh nhân đủ điều kiện chuẩn bị thực hiện kỹ thuật (nhịn ăn / lấy mẫu / an toàn CĐHA)
          </span>
        </label>
      </div>

      <div className="pt-2">
        <Button
          type="submit"
          className="w-full"
          loading={isSubmitting}
          disabled={!canSubmitReceive || isSubmitting}
        >
          <UserCheck className="h-4 w-4" aria-hidden="true" />
          <span>Tiếp nhận bệnh nhân &amp; Chuyển vào hàng đợi (READY)</span>
        </Button>
      </div>
    </form>
  );
}

export function DiagnosticPatientReceiveDetailPanel({
  order,
  clinicalContext,
  availableServicePoints,
  hasReceivePermission,
  isSubmitting,
  onReceivePatient,
}: DiagnosticPatientReceiveDetailPanelProps) {
  if (!order) {
    return (
      <Card>
        <CardContent className="py-12 text-center">
          <ClipboardCheck className="mx-auto mb-2 h-8 w-8 text-sub" aria-hidden="true" />
          <div className="text-sm font-semibold text-navy">
            Chọn một phiếu chỉ định cận lâm sàng ở bảng bên trái
          </div>
          <p className="mt-1 text-xs text-sub">
            Hệ thống sẽ hiển thị đầy đủ thông tin hành chính, bối cảnh lâm sàng, checklist 9 tiêu
            chí tiếp nhận (UC40) và biểu mẫu xác nhận bệnh nhân vào hàng đợi phòng kỹ thuật.
          </p>
        </CardContent>
      </Card>
    );
  }

  const normalizedQueueStatus = (order.queueStatus ?? 'WAITING').toUpperCase();
  const isWaitingForReception =
    order.status === 'CONFIRMED' &&
    (normalizedQueueStatus === 'WAITING' ||
      normalizedQueueStatus === 'RESERVED' ||
      normalizedQueueStatus === 'SKIPPED');
  const canSubmitReceive = isWaitingForReception && order.canReceive && hasReceivePermission;

  return (
    <Card data-testid="diagnostic-receive-detail-panel">
      <CardHeader className="border-b border-border pb-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <div className="flex items-center gap-2">
              <span className="font-mono text-sm font-bold text-navy">{order.orderCode}</span>
              {order.ticketNo && (
                <span className="rounded-md bg-sky-100 px-2 py-0.5 font-mono text-xs font-bold text-sky-800">
                  STT: {order.ticketNo}
                </span>
              )}
              <span className="font-mono text-[11px] text-sub">v{order.stateVersion}</span>
            </div>
            <div className="mt-1 text-xs font-semibold text-ink">{order.serviceName}</div>
            <div className="text-[11px] text-sub">
              Mã DV: <span className="font-mono">{order.serviceCode}</span> • Đơn giá:{' '}
              <span className="font-semibold text-navy">{formatCurrency(order.unitPrice)}</span>
            </div>
          </div>

          <div className="flex flex-col items-end gap-1">
            <Badge
              tone={
                order.status === 'ORDERED'
                  ? 'amber'
                  : order.status === 'CONFIRMED'
                    ? 'sky'
                    : order.status === 'IN_PROGRESS'
                      ? 'emerald'
                      : 'slate'
              }
            >
              {order.status === 'ORDERED'
                ? 'Chờ xác nhận (ORDERED)'
                : order.status === 'CONFIRMED'
                  ? 'Đã xác nhận (CONFIRMED)'
                  : order.status}
            </Badge>
            <Badge
              tone={
                normalizedQueueStatus === 'READY'
                  ? 'emerald'
                  : normalizedQueueStatus === 'WAITING' || normalizedQueueStatus === 'RESERVED'
                    ? 'amber'
                    : normalizedQueueStatus === 'SKIPPED'
                      ? 'rose'
                      : 'slate'
              }
            >
              Hàng đợi: {normalizedQueueStatus}
            </Badge>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4 pt-4 text-xs">
        {/* 1. Patient Demographics & Allergy Alert */}
        <section
          aria-label="Thông tin hành chính bệnh nhân"
          className="rounded-xl border border-border bg-canvas p-3.5 space-y-2"
        >
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <UserCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>{order.patient.fullName}</span>
            </div>
            <span className="font-mono text-[11px] font-semibold text-sub">
              {order.patient.patientCode}
            </span>
          </div>

          <div className="grid grid-cols-2 gap-2 text-[11px] text-sub">
            <div>
              Giới tính / Tuổi:{' '}
              <span className="font-medium text-ink">
                {order.patient.gender === 'FEMALE'
                  ? 'Nữ'
                  : order.patient.gender === 'MALE'
                    ? 'Nam'
                    : (order.patient.gender ?? 'Chưa rõ')}
                {order.patient.age ? ` (${order.patient.age} tuổi)` : ''}
              </span>
            </div>
            <div>
              Nhóm máu:{' '}
              <span className="font-mono font-semibold text-ink">
                {order.patient.bloodType ?? 'Chưa rõ'}
              </span>
            </div>
            <div>
              Điện thoại:{' '}
              <span className="font-mono text-ink">{order.patient.phone ?? 'Chưa cập nhật'}</span>
            </div>
            <div>
              Lượt khám:{' '}
              <span className="font-mono font-semibold text-navy">{order.visitCode}</span>
            </div>
          </div>

          {order.patient.allergies && (
            <div
              className="flex items-start gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-[11px] text-rose-800"
              data-testid="patient-allergy-warning"
            >
              <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
              <div>
                <span className="font-bold">Cảnh báo tiền sử dị ứng / lưu ý an toàn: </span>
                <span>{order.patient.allergies}</span>
              </div>
            </div>
          )}
        </section>

        {/* 2. Clinical Context & Ordering Doctor */}
        <section
          aria-label="Bối cảnh lâm sàng & chỉ định bác sĩ"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2"
        >
          <div className="flex items-center gap-1.5 font-bold text-navy">
            <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
            <span>Bối cảnh lâm sàng &amp; Chỉ định chuyên khoa</span>
          </div>

          <div className="space-y-1.5 text-[11px]">
            <div>
              <span className="text-sub">Bác sĩ chỉ định: </span>
              <span className="font-semibold text-ink">{order.orderingDoctorName}</span>
              {order.orderingDoctorCode && (
                <span className="font-mono text-sub"> ({order.orderingDoctorCode})</span>
              )}{' '}
              • <span className="text-sub">{order.specialtyName}</span>
            </div>

            {clinicalContext && (
              <>
                {clinicalContext.primaryDiagnosisCode && (
                  <div>
                    <span className="text-sub">Chẩn đoán ICD-10: </span>
                    <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono font-bold text-navy">
                      {clinicalContext.primaryDiagnosisCode}
                    </span>{' '}
                    <span className="font-medium text-ink">
                      {clinicalContext.primaryDiagnosisName}
                    </span>
                  </div>
                )}
                {clinicalContext.reasonForVisit && (
                  <div>
                    <span className="text-sub">Lý do khám: </span>
                    <span className="text-ink">{clinicalContext.reasonForVisit}</span>
                  </div>
                )}
                {clinicalContext.presentingComplaint && (
                  <div>
                    <span className="text-sub">Bệnh sử / Triệu chứng: </span>
                    <span className="text-ink">{clinicalContext.presentingComplaint}</span>
                  </div>
                )}
                {clinicalContext.clinicalFindings && (
                  <div>
                    <span className="text-sub">Khám lâm sàng: </span>
                    <span className="text-ink">{clinicalContext.clinicalFindings}</span>
                  </div>
                )}
              </>
            )}

            {order.instruction && (
              <div className="rounded-lg border border-sky-100 bg-sky-50/70 px-2.5 py-2 text-sky-900">
                <span className="font-semibold">Dặn dò kỹ thuật từ BS: </span>
                <span>{order.instruction}</span>
              </div>
            )}

            {order.isAdditionalOrder && (
              <div
                className="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-2 text-amber-900"
                data-testid="additional-order-context"
              >
                <div className="flex items-center gap-1 font-bold">
                  <Layers className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>
                    Chỉ định bổ sung lần #{order.orderSequenceNo}
                    {order.followUpToOrderCode ? ` (tiếp theo ${order.followUpToOrderCode})` : ''}
                  </span>
                </div>
                {order.additionalOrderReason && (
                  <p className="mt-0.5 text-[11px]">{order.additionalOrderReason}</p>
                )}
              </div>
            )}
          </div>
        </section>

        {/* 3. Eligibility Checklist (9 criteria for UC40) */}
        <section
          aria-label="Kiểm tra điều kiện tiếp nhận bệnh nhân cận lâm sàng"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2.5"
          data-testid="receive-eligibility-checklist"
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Checklist điều kiện tiếp nhận (UC40)</span>
            </div>
            <Badge tone={order.eligibility.isEligible ? 'emerald' : 'rose'}>
              {order.eligibility.isEligible ? 'Đạt 9/9 tiêu chí' : 'Có tiêu chí chưa đạt'}
            </Badge>
          </div>

          <ul className="grid grid-cols-1 gap-1.5 text-[11px]">
            {RECEIVE_ELIGIBILITY_CRITERIA.map((criterion) => {
              const passed = Boolean(order.eligibility[criterion.key]);
              return (
                <li
                  key={criterion.key}
                  className={`flex items-center gap-2 rounded-lg px-2.5 py-1.5 ${
                    passed ? 'bg-emerald-50/60 text-ink' : 'bg-rose-50 text-rose-800 font-semibold'
                  }`}
                >
                  {passed ? (
                    <CheckCircle2
                      className="h-3.5 w-3.5 shrink-0 text-emerald-600"
                      aria-hidden="true"
                    />
                  ) : (
                    <XCircle className="h-3.5 w-3.5 shrink-0 text-rose-600" aria-hidden="true" />
                  )}
                  <span>{criterion.label}</span>
                </li>
              );
            })}
          </ul>

          {!order.canReceive && order.disabledReason && (
            <Alert tone="warning">
              <div className="flex items-start gap-2" data-testid="order-receive-disabled-reason">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <div>
                  <div className="font-bold">Không thể tiếp nhận bệnh nhân cho phiếu này</div>
                  <div className="mt-0.5 text-xs">{order.disabledReason}</div>
                </div>
              </div>
            </Alert>
          )}
        </section>

        {/* 4. RBAC Permission Notice if unauthorized */}
        {!hasReceivePermission && (
          <Alert tone="error">
            <div className="flex items-start gap-2" data-testid="missing-receive-permission-alert">
              <Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">
                  Tài khoản chưa được cấp quyền tiếp nhận bệnh nhân vào hàng đợi CLS
                </div>
                <div className="mt-0.5 text-xs">
                  Bạn cần quyền <span className="font-mono">QUEUE_MANAGE</span> để xác nhận bệnh
                  nhân có mặt tại điểm dịch vụ và chuyển trạng thái hàng đợi sang{' '}
                  <span className="font-mono">READY</span>.
                </div>
              </div>
            </div>
          </Alert>
        )}

        {/* 5. Reception Form (when waiting for reception) OR Audit Summary (when already READY/IN_PROGRESS/COMPLETED) */}
        {isWaitingForReception ? (
          <DiagnosticPatientReceiveForm
            key={`${order.orderCode}-${order.queueStatus ?? 'WAITING'}-${order.stateVersion}`}
            order={order}
            availableServicePoints={availableServicePoints}
            canSubmitReceive={canSubmitReceive}
            isSubmitting={isSubmitting}
            onReceivePatient={onReceivePatient}
          />
        ) : (
          <section
            aria-label="Thông tin tiếp nhận bệnh nhân tại điểm dịch vụ"
            className="rounded-xl border border-emerald-200 bg-emerald-50/70 p-3.5 space-y-1.5 text-[11px] text-emerald-950"
            data-testid="received-patient-audit-summary"
          >
            <div className="flex items-center gap-1.5 font-bold text-emerald-900">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" aria-hidden="true" />
              <span>
                {normalizedQueueStatus === 'READY'
                  ? 'Bệnh nhân đã được tiếp nhận và sẵn sàng trong hàng đợi (READY)'
                  : `Trạng thái chỉ định / hàng đợi hiện tại: ${order.status} (${normalizedQueueStatus})`}
              </span>
            </div>
            <div>
              Nhân viên tiếp nhận:{' '}
              <span className="font-semibold">
                {order.receivedByStaffName ??
                  order.confirmedByStaffName ??
                  'Nhân viên cận lâm sàng'}
              </span>
              {(order.receivedByStaffCode ?? order.confirmedByStaffCode) && (
                <span className="font-mono">
                  {' '}
                  ({order.receivedByStaffCode ?? order.confirmedByStaffCode})
                </span>
              )}
            </div>
            <div>
              Thời điểm tiếp nhận:{' '}
              <span className="font-mono font-medium">
                {formatDateTime(order.receivedAt ?? order.readyAt ?? order.confirmedAt)}
              </span>
            </div>
            <div>
              Số thứ tự hàng đợi:{' '}
              <span className="font-mono font-bold text-emerald-800">
                {order.ticketNo ?? 'Đã cấp số'}
              </span>{' '}
              • Phòng: <span className="font-mono font-semibold">{order.roomCode ?? '--'}</span> •
              Trạng thái có mặt:{' '}
              <span className="font-mono font-semibold">{order.presenceStatus ?? 'PRESENT'}</span> •
              Phiên bản trạng thái: <span className="font-mono">v{order.stateVersion}</span>
            </div>
            {(order.receptionNote ?? order.confirmationNote) && (
              <div>
                Ghi chú tiếp nhận:{' '}
                <span className="italic">{order.receptionNote ?? order.confirmationNote}</span>
              </div>
            )}
          </section>
        )}
      </CardContent>
    </Card>
  );
}
