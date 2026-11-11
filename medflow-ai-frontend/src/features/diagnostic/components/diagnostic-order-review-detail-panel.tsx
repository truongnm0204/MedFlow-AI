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
  CONFIRMATION_NOTE_PRESETS,
  confirmDiagnosticOrderSchema,
} from '../schemas/diagnostic-order-review.schema';
import type {
  ConfirmDiagnosticOrderRequest,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticOrderReviewItemDto,
  DiagnosticPriority,
  DiagnosticServicePointOptionDto,
} from '../types/diagnostic.types';

interface DiagnosticOrderReviewDetailPanelProps {
  order: DiagnosticOrderReviewItemDto | null;
  clinicalContext?: DiagnosticOrderClinicalSummaryDto | null;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  hasConfirmPermission: boolean;
  isSubmitting: boolean;
  onConfirmOrder: (orderCode: string, payload: ConfirmDiagnosticOrderRequest) => void;
}

const ELIGIBILITY_CRITERIA: Array<{
  key: keyof DiagnosticOrderReviewItemDto['eligibility'];
  label: string;
}> = [
  {
    key: 'isOrderPendingConfirmation',
    label: 'Phiếu chỉ định ở trạng thái chờ xác nhận (ORDERED)',
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
    key: 'isDoctorAuthorizedForService',
    label: 'Bác sĩ chỉ định có chứng chỉ & quyền chuyên khoa hợp lệ',
  },
  {
    key: 'isServicePointActive',
    label: 'Điểm phục vụ kỹ thuật (Service Point) sẵn sàng',
  },
  {
    key: 'isRoomActive',
    label: 'Phòng thực hiện cận lâm sàng đang mở cửa hoạt động',
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

interface DiagnosticOrderConfirmFormProps {
  order: DiagnosticOrderReviewItemDto;
  availableServicePoints: DiagnosticServicePointOptionDto[];
  canSubmitConfirm: boolean;
  isSubmitting: boolean;
  onConfirmOrder: (orderCode: string, payload: ConfirmDiagnosticOrderRequest) => void;
}

function DiagnosticOrderConfirmForm({
  order,
  availableServicePoints,
  canSubmitConfirm,
  isSubmitting,
  onConfirmOrder,
}: DiagnosticOrderConfirmFormProps) {
  const initialPriority = (order.priority?.toUpperCase() || 'NORMAL') as DiagnosticPriority;

  const [selectedServicePointId, setSelectedServicePointId] = useState<number | ''>(
    order.servicePointId ?? '',
  );
  const [priority, setPriority] = useState<DiagnosticPriority>(
    initialPriority === 'URGENT' || initialPriority === 'STAT' ? initialPriority : 'NORMAL',
  );
  const [patientPresent, setPatientPresent] = useState(order.presenceStatus !== 'ABSENT');
  const [instruction, setInstruction] = useState(order.instruction ?? '');
  const [confirmationNote, setConfirmationNote] = useState(order.confirmationNote ?? '');
  const [isEligibilityVerified, setIsEligibilityVerified] = useState(true);
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
      isEligibilityVerified,
      servicePointId:
        selectedServicePointId !== '' ? Number(selectedServicePointId) : order.servicePointId,
      roomId: chosenSp?.roomId ?? order.roomId ?? null,
      priority,
      instruction: instruction.trim() ? instruction : null,
      confirmationNote: confirmationNote.trim() ? confirmationNote : null,
      patientPresent,
      expectedStateVersion: order.stateVersion || 1,
    };

    const parsed = confirmDiagnosticOrderSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(
        firstIssue?.message ?? 'Dữ liệu xác nhận chỉ định không hợp lệ. Vui lòng kiểm tra lại.',
      );
      return;
    }

    onConfirmOrder(order.orderCode, {
      isEligibilityVerified: parsed.data.isEligibilityVerified,
      eligibilityConfirmed: parsed.data.isEligibilityVerified,
      servicePointId: parsed.data.servicePointId ?? null,
      roomId: parsed.data.roomId ?? null,
      priority: parsed.data.priority ?? priority,
      instruction: parsed.data.instruction ?? null,
      confirmationNote: parsed.data.confirmationNote ?? null,
      patientPresent: parsed.data.patientPresent ?? true,
      expectedStateVersion: parsed.data.expectedStateVersion ?? order.stateVersion,
    });
  };

  return (
    <form
      onSubmit={handleFormSubmit}
      className="space-y-3 rounded-xl border border-border bg-canvas p-3.5"
      data-testid="confirm-order-form"
    >
      <div className="font-bold text-navy">
        Thông tin xác nhận & Phân luồng hàng đợi cận lâm sàng
      </div>

      {validationError && (
        <Alert tone="error">
          <span data-testid="confirm-form-validation-error">{validationError}</span>
        </Alert>
      )}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label
            htmlFor="confirm-service-point"
            className="block text-[11px] font-semibold text-navy"
          >
            Điểm phục vụ / Phòng thực hiện
          </label>
          <select
            id="confirm-service-point"
            aria-label="Điểm phục vụ / Phòng thực hiện"
            value={selectedServicePointId}
            disabled={!canSubmitConfirm || isSubmitting}
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
          <label htmlFor="confirm-priority" className="block text-[11px] font-semibold text-navy">
            Mức độ ưu tiên hàng đợi
          </label>
          <select
            id="confirm-priority"
            aria-label="Mức độ ưu tiên hàng đợi"
            value={priority}
            disabled={!canSubmitConfirm || isSubmitting}
            onChange={(e) => setPriority(e.target.value as DiagnosticPriority)}
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
          >
            <option value="NORMAL">NORMAL — Bình thường</option>
            <option value="URGENT">URGENT — Khẩn</option>
            <option value="STAT">STAT — Cấp cứu tối khẩn</option>
          </select>
        </div>
      </div>

      <div>
        <label htmlFor="confirm-instruction" className="block text-[11px] font-semibold text-navy">
          Hướng dẫn kỹ thuật / Chuẩn bị bệnh nhân (tối đa 1000 ký tự)
        </label>
        <textarea
          id="confirm-instruction"
          aria-label="Hướng dẫn kỹ thuật / Chuẩn bị bệnh nhân"
          rows={2}
          value={instruction}
          disabled={!canSubmitConfirm || isSubmitting}
          onChange={(e) => setInstruction(e.target.value)}
          placeholder="Nhập hướng dẫn lấy mẫu hoặc chuẩn bị chụp chiếu..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label htmlFor="confirm-note" className="block text-[11px] font-semibold text-navy">
          Ghi chú xác nhận chỉ định (tối đa 500 ký tự)
        </label>
        <textarea
          id="confirm-note"
          aria-label="Ghi chú xác nhận chỉ định"
          rows={2}
          value={confirmationNote}
          disabled={!canSubmitConfirm || isSubmitting}
          onChange={(e) => setConfirmationNote(e.target.value)}
          placeholder="Ghi chú đối chiếu hành chính, tình trạng bệnh nhân khi tiếp nhận..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {CONFIRMATION_NOTE_PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              disabled={!canSubmitConfirm || isSubmitting}
              onClick={() => setConfirmationNote(preset)}
              className="rounded-md border border-border bg-white px-2 py-1 text-[10px] font-medium text-sub transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
            >
              + {preset.slice(0, 42)}...
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-2 pt-1">
        <label className="flex items-center gap-2 text-xs text-ink">
          <input
            type="checkbox"
            checked={patientPresent}
            disabled={!canSubmitConfirm || isSubmitting}
            onChange={(e) => setPatientPresent(e.target.checked)}
            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>Bệnh nhân đã có mặt tại khu vực cận lâm sàng (PRESENT)</span>
        </label>

        <label className="flex items-start gap-2 text-xs font-semibold text-navy">
          <input
            type="checkbox"
            aria-label="Tôi đã kiểm tra đối chiếu thông tin bệnh nhân và xác nhận phiếu chỉ định đủ điều kiện"
            checked={isEligibilityVerified}
            disabled={!canSubmitConfirm || isSubmitting}
            onChange={(e) => setIsEligibilityVerified(e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>
            Tôi đã kiểm tra đối chiếu thông tin bệnh nhân và xác nhận phiếu chỉ định đủ điều kiện
            thực hiện
          </span>
        </label>
      </div>

      <div className="pt-2">
        <Button
          type="submit"
          className="w-full"
          loading={isSubmitting}
          disabled={!canSubmitConfirm || isSubmitting}
        >
          <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
          <span>Xác nhận chỉ định & Xếp hàng đợi</span>
        </Button>
      </div>
    </form>
  );
}

export function DiagnosticOrderReviewDetailPanel({
  order,
  clinicalContext,
  availableServicePoints,
  hasConfirmPermission,
  isSubmitting,
  onConfirmOrder,
}: DiagnosticOrderReviewDetailPanelProps) {
  if (!order) {
    return (
      <Card>
        <CardContent className="py-12 text-center">
          <ClipboardCheck className="mx-auto mb-2 h-8 w-8 text-sub" aria-hidden="true" />
          <div className="text-sm font-semibold text-navy">
            Chọn một phiếu chỉ định cận lâm sàng ở bảng bên trái
          </div>
          <p className="mt-1 text-xs text-sub">
            Hệ thống sẽ hiển thị đầy đủ thông tin lâm sàng, kiểm tra điều kiện hợp lệ (UC39) và biểu
            mẫu xác nhận tiếp nhận vào hàng đợi.
          </p>
        </CardContent>
      </Card>
    );
  }

  const isPendingOrder = order.status === 'ORDERED';
  const canSubmitConfirm = isPendingOrder && order.canConfirm && hasConfirmPermission;

  return (
    <Card data-testid="diagnostic-review-detail-panel">
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
            <span>Bối cảnh lâm sàng & Chỉ định chuyên khoa</span>
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

        {/* 3. Eligibility Checklist (8 criteria) */}
        <section
          aria-label="Kiểm tra điều kiện hợp lệ của chỉ định"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2.5"
          data-testid="eligibility-checklist"
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Checklist điều kiện hợp lệ (UC39)</span>
            </div>
            <Badge tone={order.eligibility.isEligible ? 'emerald' : 'rose'}>
              {order.eligibility.isEligible ? 'Đạt 8/8 tiêu chí' : 'Có tiêu chí chưa đạt'}
            </Badge>
          </div>

          <ul className="grid grid-cols-1 gap-1.5 text-[11px]">
            {ELIGIBILITY_CRITERIA.map((criterion) => {
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

          {!order.canConfirm && order.disabledReason && (
            <Alert tone="warning">
              <div className="flex items-start gap-2" data-testid="order-disabled-reason">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <div>
                  <div className="font-bold">Không thể xác nhận phiếu chỉ định này</div>
                  <div className="mt-0.5 text-xs">{order.disabledReason}</div>
                </div>
              </div>
            </Alert>
          )}
        </section>

        {/* 4. RBAC Permission Notice if unauthorized */}
        {!hasConfirmPermission && (
          <Alert tone="error">
            <div className="flex items-start gap-2" data-testid="missing-confirm-permission-alert">
              <Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">Tài khoản chưa được cấp quyền xác nhận chỉ định CLS</div>
                <div className="mt-0.5 text-xs">
                  Bạn cần một trong các quyền <span className="font-mono">QUEUE_MANAGE</span>,{' '}
                  <span className="font-mono">DIAGNOSTIC_RESULT_WRITE</span> hoặc{' '}
                  <span className="font-mono">DIAGNOSTIC_ORDER</span> để xác nhận phiếu chỉ định.
                </div>
              </div>
            </div>
          </Alert>
        )}

        {/* 5. Confirmation Form (when ORDERED) OR Audit Summary (when already CONFIRMED/IN_PROGRESS/COMPLETED) */}
        {isPendingOrder ? (
          <DiagnosticOrderConfirmForm
            key={`${order.orderCode}-${order.status}-${order.stateVersion}`}
            order={order}
            availableServicePoints={availableServicePoints}
            canSubmitConfirm={canSubmitConfirm}
            isSubmitting={isSubmitting}
            onConfirmOrder={onConfirmOrder}
          />
        ) : (
          <section
            aria-label="Thông tin xác nhận chỉ định"
            className="rounded-xl border border-emerald-200 bg-emerald-50/70 p-3.5 space-y-1.5 text-[11px] text-emerald-950"
            data-testid="confirmed-order-audit-summary"
          >
            <div className="flex items-center gap-1.5 font-bold text-emerald-900">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" aria-hidden="true" />
              <span>Phiếu chỉ định đã được xác nhận hợp lệ</span>
            </div>
            <div>
              Nhân viên xác nhận:{' '}
              <span className="font-semibold">
                {order.confirmedByStaffName ?? 'Nhân viên cận lâm sàng'}
              </span>
              {order.confirmedByStaffCode && (
                <span className="font-mono"> ({order.confirmedByStaffCode})</span>
              )}
            </div>
            <div>
              Thời điểm xác nhận:{' '}
              <span className="font-mono font-medium">{formatDateTime(order.confirmedAt)}</span>
            </div>
            <div>
              Số thứ tự hàng đợi:{' '}
              <span className="font-mono font-bold text-emerald-800">
                {order.ticketNo ?? 'Đã cấp số'}
              </span>{' '}
              • Phòng: <span className="font-mono font-semibold">{order.roomCode ?? '--'}</span> •
              Phiên bản trạng thái: <span className="font-mono">v{order.stateVersion}</span>
            </div>
            {order.confirmationNote && (
              <div>
                Ghi chú xác nhận: <span className="italic">{order.confirmationNote}</span>
              </div>
            )}
          </section>
        )}
      </CardContent>
    </Card>
  );
}
