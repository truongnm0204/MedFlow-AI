import {
  AlertTriangle,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  History,
  Lock,
  PauseCircle,
  Play,
  ShieldAlert,
  Stethoscope,
  XCircle,
} from 'lucide-react';
import { useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Input } from '@/shared/ui/input';

import {
  CANCEL_EXECUTION_REASON_PRESETS,
  EXECUTION_REASON_PRESETS,
  updateDiagnosticServiceStatusSchema,
} from '../schemas/diagnostic-execution.schema';
import type {
  DiagnosticExecutionTargetStatus,
  DiagnosticServiceExecutionDetailResponseDto,
  DiagnosticServiceExecutionItemDto,
  UpdateDiagnosticServiceStatusRequest,
} from '../types/diagnostic-execution.types';
import type { DiagnosticOrderClinicalSummaryDto } from '../types/diagnostic-order-review.types';

interface DiagnosticExecutionDetailPanelProps {
  order: DiagnosticServiceExecutionItemDto | null;
  detail: DiagnosticServiceExecutionDetailResponseDto | null;
  clinicalContext?: DiagnosticOrderClinicalSummaryDto | null;
  isLoading: boolean;
  isBusy: boolean;
  hasUpdatePermission: boolean;
  onStart: (orderCode: string, payload: UpdateDiagnosticServiceStatusRequest) => void;
  onComplete: (orderCode: string, payload: UpdateDiagnosticServiceStatusRequest) => void;
  onUpdateStatus: (orderCode: string, payload: UpdateDiagnosticServiceStatusRequest) => void;
}

const EXECUTION_ELIGIBILITY_CRITERIA: Array<{
  key: keyof DiagnosticServiceExecutionItemDto['eligibility'];
  label: string;
}> = [
  {
    key: 'isOrderConfirmedOrInProgress',
    label: 'Phiếu chỉ định đã xác nhận hoặc đang thực hiện (CONFIRMED / IN_PROGRESS)',
  },
  {
    key: 'isQueueEligible',
    label: 'Lượt hàng đợi cận lâm sàng đang hoạt động hợp lệ (không SKIPPED / CANCELLED)',
  },
  { key: 'isVisitActive', label: 'Lượt khám lâm sàng đang hoạt động hợp lệ' },
  { key: 'isPatientActive', label: 'Hồ sơ bệnh nhân đang hoạt động (ACTIVE)' },
  { key: 'isServiceActive', label: 'Danh mục dịch vụ cận lâm sàng đang mở' },
  { key: 'isDepartmentActive', label: 'Khoa/Phòng phụ trách chuyên môn đang hoạt động' },
  { key: 'isServicePointActive', label: 'Điểm phục vụ kỹ thuật (Service Point) sẵn sàng' },
  { key: 'isRoomActive', label: 'Phòng thực hiện cận lâm sàng đang mở cửa hoạt động' },
  {
    key: 'isResultNotFinalized',
    label: 'Kết quả cận lâm sàng chưa được kết thúc (chưa FINALIZED)',
  },
  {
    key: 'hasExecutionPermission',
    label:
      'Nhân viên có quyền cập nhật trạng thái thực hiện (QUEUE_MANAGE | DIAGNOSTIC_RESULT_WRITE)',
  },
];

const PAUSE_TARGET_OPTIONS: Array<{ value: DiagnosticExecutionTargetStatus; label: string }> = [
  { value: 'CONFIRMED', label: 'CONFIRMED — Về đã xác nhận' },
  { value: 'READY', label: 'READY — Về sẵn sàng trong hàng đợi' },
  { value: 'WAITING', label: 'WAITING — Về chờ tiếp nhận' },
];

function formatHistoryTime(iso?: string | null): string {
  if (!iso) return '';
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('vi-VN', {
    style: 'currency',
    currency: 'VND',
    maximumFractionDigits: 0,
  }).format(amount);
}

interface PauseOrCancelFormProps {
  order: DiagnosticServiceExecutionItemDto;
  mode: 'pause' | 'cancel';
  isBusy: boolean;
  onSubmit: (payload: UpdateDiagnosticServiceStatusRequest) => void;
  onClose: () => void;
}

function PauseOrCancelForm({ order, mode, isBusy, onSubmit, onClose }: PauseOrCancelFormProps) {
  const [targetStatus, setTargetStatus] = useState<DiagnosticExecutionTargetStatus>(
    mode === 'cancel' ? 'CANCELLED' : 'READY',
  );
  const [reason, setReason] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const presets = mode === 'cancel' ? CANCEL_EXECUTION_REASON_PRESETS : EXECUTION_REASON_PRESETS;
  const title =
    mode === 'cancel' ? 'Hủy thực hiện kỹ thuật phiếu chỉ định' : 'Tạm dừng thực hiện về hàng đợi';
  const submitLabel = mode === 'cancel' ? 'Xác nhận hủy thực hiện' : 'Xác nhận tạm dừng';

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const candidate = {
      targetStatus,
      reason: reason.trim() || null,
      expectedStateVersion: order.stateVersion || 0,
    };
    const parsed = updateDiagnosticServiceStatusSchema.safeParse(candidate);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(
        firstIssue?.message ?? 'Dữ liệu cập nhật trạng thái thực hiện không hợp lệ.',
      );
      return;
    }

    onSubmit({
      targetStatus: parsed.data.targetStatus,
      status: parsed.data.targetStatus,
      reason: parsed.data.reason ?? null,
      expectedStateVersion: parsed.data.expectedStateVersion ?? order.stateVersion,
    });
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="space-y-3 rounded-xl border border-amber-200 bg-amber-50/60 p-3.5"
      data-testid={`execution-${mode}-form`}
    >
      <div className="font-bold text-navy">{title}</div>

      {validationError && (
        <Alert tone="error">
          <span data-testid="execution-form-validation-error">{validationError}</span>
        </Alert>
      )}

      {mode === 'pause' && (
        <div>
          <label
            htmlFor="execution-pause-target"
            className="block text-[11px] font-semibold text-navy"
          >
            Trạng thái đích sau khi tạm dừng
          </label>
          <select
            id="execution-pause-target"
            aria-label="Trạng thái đích tạm dừng"
            value={targetStatus}
            disabled={isBusy}
            onChange={(e) => setTargetStatus(e.target.value as DiagnosticExecutionTargetStatus)}
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
          >
            {PAUSE_TARGET_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
      )}

      <div>
        <label
          htmlFor={`execution-${mode}-reason`}
          className="block text-[11px] font-semibold text-navy"
        >
          Lý do {mode === 'cancel' ? 'hủy' : 'tạm dừng'} (bắt buộc, 5–500 ký tự)
        </label>
        <textarea
          id={`execution-${mode}-reason`}
          aria-label={`Lý do ${mode === 'cancel' ? 'hủy' : 'tạm dừng'} thực hiện kỹ thuật`}
          rows={3}
          value={reason}
          disabled={isBusy}
          onChange={(e) => setReason(e.target.value)}
          placeholder={
            mode === 'cancel'
              ? 'Nhập lý do hủy thực hiện kỹ thuật...'
              : 'Nhập lý do tạm dừng thực hiện về hàng đợi...'
          }
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {presets.map((preset) => (
            <button
              key={preset}
              type="button"
              disabled={isBusy}
              onClick={() => setReason(preset)}
              className="rounded-md border border-border bg-white px-2 py-1 text-[10px] font-medium text-sub transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
            >
              + {preset.slice(0, 44)}...
            </button>
          ))}
        </div>
      </div>

      <div className="flex items-center gap-2 pt-1">
        <Button type="submit" size="sm" loading={isBusy} disabled={isBusy}>
          {mode === 'cancel' ? (
            <XCircle className="h-4 w-4" aria-hidden="true" />
          ) : (
            <PauseCircle className="h-4 w-4" aria-hidden="true" />
          )}
          <span>{submitLabel}</span>
        </Button>
        <button
          type="button"
          onClick={onClose}
          disabled={isBusy}
          className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-sub hover:bg-canvas disabled:opacity-60"
        >
          Đóng
        </button>
      </div>
    </form>
  );
}

export function DiagnosticExecutionDetailPanel({
  order,
  detail,
  clinicalContext,
  isLoading,
  isBusy,
  hasUpdatePermission,
  onStart,
  onComplete,
  onUpdateStatus,
}: DiagnosticExecutionDetailPanelProps) {
  const [specimenCode, setSpecimenCode] = useState(order?.specimenCode ?? '');
  const [specimenType, setSpecimenType] = useState(order?.specimenType ?? '');
  const [equipmentCode, setEquipmentCode] = useState(order?.equipmentCode ?? '');
  const [executionNote, setExecutionNote] = useState(order?.executionNote ?? '');
  const [activeForm, setActiveForm] = useState<'pause' | 'cancel' | null>(null);

  if (!order) {
    return (
      <Card>
        <CardContent className="py-12 text-center">
          <ClipboardCheck className="mx-auto mb-2 h-8 w-8 text-sub" aria-hidden="true" />
          <div className="text-sm font-semibold text-navy">
            Chọn một phiếu chỉ định cận lâm sàng ở bảng bên trái
          </div>
          <p className="mt-1 text-xs text-sub">
            Hệ thống sẽ hiển thị bối cảnh lâm sàng, checklist điều kiện thực hiện (UC42), biểu mẫu
            cập nhật trạng thái kỹ thuật và lịch sử chuyển trạng thái.
          </p>
        </CardContent>
      </Card>
    );
  }

  const normalizedStatus = order.status.toUpperCase();
  const normalizedQueueStatus = (order.queueStatus ?? 'WAITING').toUpperCase();
  const canSubmit = order.canUpdateStatus && hasUpdatePermission && !isBusy;
  const history = detail?.executionHistory ?? [];
  const context = clinicalContext ?? detail?.clinicalContext ?? null;

  const buildTechnicalPayload = (): Pick<
    UpdateDiagnosticServiceStatusRequest,
    'specimenCode' | 'specimenType' | 'equipmentCode' | 'executionNote'
  > => ({
    specimenCode: specimenCode.trim() || null,
    specimenType: specimenType.trim() || null,
    equipmentCode: equipmentCode.trim() || null,
    executionNote: executionNote.trim() || null,
  });

  const handleStart = () => {
    onStart(order.orderCode, {
      targetStatus: 'IN_PROGRESS',
      expectedStateVersion: order.stateVersion,
      ...buildTechnicalPayload(),
    });
  };

  const handleComplete = () => {
    onComplete(order.orderCode, {
      targetStatus: 'COMPLETED',
      expectedStateVersion: order.stateVersion,
      ...buildTechnicalPayload(),
    });
  };

  return (
    <Card data-testid="diagnostic-execution-detail-panel">
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
                normalizedStatus === 'ORDERED'
                  ? 'amber'
                  : normalizedStatus === 'CONFIRMED'
                    ? 'sky'
                    : normalizedStatus === 'IN_PROGRESS'
                      ? 'emerald'
                      : normalizedStatus === 'CANCELLED'
                        ? 'rose'
                        : 'slate'
              }
            >
              {order.statusLabel ?? normalizedStatus}
            </Badge>
            <Badge
              tone={
                normalizedQueueStatus === 'READY' || normalizedQueueStatus === 'IN_PROGRESS'
                  ? 'emerald'
                  : normalizedQueueStatus === 'WAITING' || normalizedQueueStatus === 'RESERVED'
                    ? 'amber'
                    : normalizedQueueStatus === 'SKIPPED' || normalizedQueueStatus === 'CANCELLED'
                      ? 'rose'
                      : 'slate'
              }
            >
              HĐ: {order.queueStatusLabel ?? normalizedQueueStatus}
            </Badge>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4 pt-4 text-xs">
        {/* 1. Patient & Clinical Context */}
        <section
          aria-label="Thông tin bệnh nhân & bối cảnh lâm sàng"
          className="rounded-xl border border-border bg-canvas p-3.5 space-y-2"
        >
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
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

          {context && (
            <div className="space-y-1 text-[11px]">
              {context.primaryDiagnosisCode && (
                <div>
                  <span className="text-sub">Chẩn đoán ICD-10: </span>
                  <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono font-bold text-navy">
                    {context.primaryDiagnosisCode}
                  </span>{' '}
                  <span className="font-medium text-ink">{context.primaryDiagnosisName}</span>
                </div>
              )}
              {context.reasonForVisit && (
                <div>
                  <span className="text-sub">Lý do khám: </span>
                  <span className="text-ink">{context.reasonForVisit}</span>
                </div>
              )}
            </div>
          )}

          {order.instruction && (
            <div className="rounded-lg border border-sky-100 bg-sky-50/70 px-2.5 py-2 text-sky-900">
              <span className="font-semibold">Dặn dò kỹ thuật từ BS: </span>
              <span>{order.instruction}</span>
            </div>
          )}
        </section>

        {/* 2. Eligibility Checklist (UC42) */}
        <section
          aria-label="Kiểm tra điều kiện thực hiện kỹ thuật"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2.5"
          data-testid="execution-eligibility"
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Checklist điều kiện thực hiện (UC42)</span>
            </div>
            <Badge tone={order.eligibility.isEligible ? 'emerald' : 'rose'}>
              {order.eligibility.isEligible ? 'Đạt tiêu chí thực hiện' : 'Có tiêu chí chưa đạt'}
            </Badge>
          </div>

          <ul className="grid grid-cols-1 gap-1.5 text-[11px]">
            {EXECUTION_ELIGIBILITY_CRITERIA.map((criterion) => {
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

          {!order.canUpdateStatus && order.disabledReason && (
            <Alert tone="warning">
              <div className="flex items-start gap-2" data-testid="execution-disabled-reason">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <div>
                  <div className="font-bold">
                    Không thể cập nhật trạng thái thực hiện cho phiếu này
                  </div>
                  <div className="mt-0.5 text-xs">{order.disabledReason}</div>
                </div>
              </div>
            </Alert>
          )}

          {order.allowedTargetStatuses.length > 0 && (
            <div className="flex flex-wrap items-center gap-1.5 text-[10px]">
              <span className="text-sub">Trạng thái đích được phép:</span>
              {order.allowedTargetStatuses.map((target) => (
                <span
                  key={target}
                  className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-mono font-semibold text-navy"
                >
                  {target}
                </span>
              ))}
            </div>
          )}
        </section>

        {/* 3. RBAC Permission Notice */}
        {!hasUpdatePermission && (
          <Alert tone="error">
            <div
              className="flex items-start gap-2"
              data-testid="missing-execution-permission-alert"
            >
              <Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">
                  Tài khoản chưa được cấp quyền cập nhật trạng thái thực hiện kỹ thuật
                </div>
                <div className="mt-0.5 text-xs">
                  Bạn cần quyền <span className="font-mono">QUEUE_MANAGE</span> hoặc{' '}
                  <span className="font-mono">DIAGNOSTIC_RESULT_WRITE</span> để bắt đầu / hoàn tất /
                  tạm dừng / hủy thực hiện phiếu chỉ định cận lâm sàng.
                </div>
              </div>
            </div>
          </Alert>
        )}

        {/* 4. Technical Fields Form */}
        <section
          aria-label="Thông tin kỹ thuật thực hiện"
          className="rounded-xl border border-border bg-white p-3.5 space-y-3"
          data-testid="execution-technical-fields"
        >
          <div className="font-bold text-navy">Thông tin kỹ thuật thực hiện</div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div>
              <label
                htmlFor="execution-specimen-code"
                className="block text-[11px] font-semibold text-navy"
              >
                Mã bệnh phẩm
              </label>
              <Input
                id="execution-specimen-code"
                aria-label="Mã bệnh phẩm"
                value={specimenCode}
                disabled={
                  !canSubmit || normalizedStatus === 'COMPLETED' || normalizedStatus === 'CANCELLED'
                }
                onChange={(e) => setSpecimenCode(e.target.value)}
                placeholder="VD: SPC-2026-0061"
                className="mt-1 h-8 text-xs"
              />
            </div>
            <div>
              <label
                htmlFor="execution-specimen-type"
                className="block text-[11px] font-semibold text-navy"
              >
                Loại bệnh phẩm
              </label>
              <Input
                id="execution-specimen-type"
                aria-label="Loại bệnh phẩm"
                value={specimenType}
                disabled={
                  !canSubmit || normalizedStatus === 'COMPLETED' || normalizedStatus === 'CANCELLED'
                }
                onChange={(e) => setSpecimenType(e.target.value)}
                placeholder="VD: Máu tĩnh mạch"
                className="mt-1 h-8 text-xs"
              />
            </div>
            <div>
              <label
                htmlFor="execution-equipment-code"
                className="block text-[11px] font-semibold text-navy"
              >
                Mã thiết bị
              </label>
              <Input
                id="execution-equipment-code"
                aria-label="Mã thiết bị"
                value={equipmentCode}
                disabled={
                  !canSubmit || normalizedStatus === 'COMPLETED' || normalizedStatus === 'CANCELLED'
                }
                onChange={(e) => setEquipmentCode(e.target.value)}
                placeholder="VD: US-ALOKA-02"
                className="mt-1 h-8 text-xs"
              />
            </div>
          </div>
          <div>
            <label htmlFor="execution-note" className="block text-[11px] font-semibold text-navy">
              Ghi chú thực hiện kỹ thuật
            </label>
            <textarea
              id="execution-note"
              aria-label="Ghi chú thực hiện kỹ thuật"
              rows={2}
              value={executionNote}
              disabled={
                !canSubmit || normalizedStatus === 'COMPLETED' || normalizedStatus === 'CANCELLED'
              }
              onChange={(e) => setExecutionNote(e.target.value)}
              placeholder="Ghi chú quá trình lấy mẫu / chụp chiếu / thăm dò..."
              className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
            />
          </div>
        </section>

        {/* 5. Action Buttons */}
        <section
          aria-label="Thao tác cập nhật trạng thái thực hiện"
          className="space-y-2"
          data-testid="execution-actions"
        >
          {order.canStartExecution && (
            <Button
              type="button"
              className="w-full"
              loading={isBusy}
              disabled={!canSubmit}
              onClick={handleStart}
              data-testid="execution-start-button"
            >
              <Play className="h-4 w-4" aria-hidden="true" />
              <span>Bắt đầu thực hiện kỹ thuật (IN_PROGRESS)</span>
            </Button>
          )}

          {order.canCompleteExecution && (
            <Button
              type="button"
              className="w-full"
              loading={isBusy}
              disabled={!canSubmit}
              onClick={handleComplete}
              data-testid="execution-complete-button"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              <span>Hoàn tất thực hiện kỹ thuật (COMPLETED)</span>
            </Button>
          )}

          {(order.canPauseExecution || order.canCancelExecution) && !activeForm && (
            <div className="grid grid-cols-2 gap-2">
              {order.canPauseExecution && (
                <button
                  type="button"
                  disabled={!canSubmit}
                  onClick={() => setActiveForm('pause')}
                  data-testid="execution-pause-button"
                  className="inline-flex items-center justify-center gap-1.5 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-800 transition-colors hover:bg-amber-100 disabled:opacity-60"
                >
                  <PauseCircle className="h-4 w-4" aria-hidden="true" />
                  <span>Tạm dừng về hàng đợi</span>
                </button>
              )}
              {order.canCancelExecution && (
                <button
                  type="button"
                  disabled={!canSubmit}
                  onClick={() => setActiveForm('cancel')}
                  data-testid="execution-cancel-button"
                  className="inline-flex items-center justify-center gap-1.5 rounded-lg border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-800 transition-colors hover:bg-rose-100 disabled:opacity-60"
                >
                  <XCircle className="h-4 w-4" aria-hidden="true" />
                  <span>Hủy thực hiện</span>
                </button>
              )}
            </div>
          )}

          {activeForm && (
            <PauseOrCancelForm
              order={order}
              mode={activeForm}
              isBusy={isBusy}
              onClose={() => setActiveForm(null)}
              onSubmit={(payload) => {
                setActiveForm(null);
                onUpdateStatus(order.orderCode, payload);
              }}
            />
          )}
        </section>

        {/* 6. Execution History Timeline */}
        <section
          aria-label="Lịch sử chuyển trạng thái thực hiện"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2"
          data-testid="execution-history"
        >
          <div className="flex items-center gap-1.5 font-bold text-navy">
            <History className="h-4 w-4 text-primary" aria-hidden="true" />
            <span>Lịch sử chuyển trạng thái thực hiện</span>
          </div>

          {isLoading ? (
            <div className="py-4 text-center text-[11px] text-sub">
              Đang tải lịch sử chuyển trạng thái...
            </div>
          ) : history.length === 0 ? (
            <div className="py-4 text-center text-[11px] text-sub">
              Chưa ghi nhận lịch sử chuyển trạng thái thực hiện cho phiếu này.
            </div>
          ) : (
            <ul className="space-y-2">
              {history.map((entry) => (
                <li
                  key={entry.auditLogId}
                  className="rounded-lg border border-border bg-canvas/60 px-2.5 py-2"
                  data-testid={`execution-history-item-${entry.auditLogId}`}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="font-semibold text-navy">{entry.actionLabel}</div>
                    <span className="font-mono text-[10px] text-sub">
                      {formatHistoryTime(entry.occurredAtUtc)}
                    </span>
                  </div>
                  <div className="mt-1 text-[11px] text-sub">
                    <span className="font-mono">{entry.previousStatus}</span>
                    {' → '}
                    <span className="font-mono font-semibold text-navy">{entry.currentStatus}</span>
                    {entry.previousQueueStatus && entry.currentQueueStatus && (
                      <span>
                        {' '}
                        • HĐ: <span className="font-mono">{entry.previousQueueStatus}</span> →{' '}
                        <span className="font-mono">{entry.currentQueueStatus}</span>
                      </span>
                    )}
                    {entry.performedByStaffName && (
                      <span>
                        {' '}
                        • Bởi: <span className="font-medium">{entry.performedByStaffName}</span>
                      </span>
                    )}{' '}
                    • <span className="font-mono">v{entry.stateVersion}</span>
                  </div>
                  {entry.reason && (
                    <div className="mt-1 text-[11px] italic text-sub">Lý do: {entry.reason}</div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      </CardContent>
    </Card>
  );
}
