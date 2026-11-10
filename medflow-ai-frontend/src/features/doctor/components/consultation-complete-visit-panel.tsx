import {
  CalendarCheck,
  CheckCircle2,
  ClipboardCheck,
  FileCheck2,
  Lock,
  RotateCcw,
  ShieldAlert,
  ShieldCheck,
} from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { completeVisitSchema } from '../schemas/consultation-workspace.schema';
import type {
  CompleteVisitRequest,
  ConsultationClinicalContextDto,
  ConsultationVisitSummaryDto,
  ConsultationWorkspaceState,
} from '../types/consultation-workspace.types';

export interface ConsultationCompleteVisitPanelProps {
  workspaceState: ConsultationWorkspaceState | (string & {});
  visit: ConsultationVisitSummaryDto;
  clinicalContext: ConsultationClinicalContextDto;
  canCompleteVisit: boolean;
  hasWritePermission?: boolean;
  disabledReason?: string | null;
  expectedStateVersion?: number | null;
  expectedRevisionNo?: number | null;
  isSubmitting?: boolean;
  submitError?: string | null;
  isFormOpen?: boolean;
  onToggleForm?: (open: boolean) => void;
  onSubmitCompleteVisit?: (
    payload: CompleteVisitRequest,
  ) => Promise<boolean | void> | boolean | void;
  onClearSubmitError?: () => void;
}

export function ConsultationCompleteVisitPanel({
  workspaceState,
  visit,
  clinicalContext,
  canCompleteVisit,
  hasWritePermission = true,
  disabledReason = null,
  expectedStateVersion = null,
  expectedRevisionNo = null,
  isSubmitting = false,
  submitError = null,
  isFormOpen: controlledIsFormOpen,
  onToggleForm,
  onSubmitCompleteVisit,
  onClearSubmitError,
}: ConsultationCompleteVisitPanelProps) {
  const isFinalized =
    clinicalContext.medicalRecordStatus === 'FINALIZED' || workspaceState === 'COMPLETED';
  const isActionable = canCompleteVisit && hasWritePermission && !isFinalized;

  const [internalFormOpen, setInternalFormOpen] = useState(false);
  const isFormOpen = controlledIsFormOpen ?? internalFormOpen;

  const setFormOpen = (nextOpen: boolean) => {
    if (onToggleForm) {
      onToggleForm(nextOpen);
    } else {
      setInternalFormOpen(nextOpen);
    }
  };

  const syncKey = `${visit.visitCode}:${clinicalContext.medicalRecordId ?? 'none'}:${clinicalContext.currentRevisionNo ?? 0}:${clinicalContext.medicalRecordStatus}`;
  const [lastSyncKey, setLastSyncKey] = useState(syncKey);

  const [clinicalConclusionInput, setClinicalConclusionInput] = useState(
    () => clinicalContext.clinicalConclusion ?? '',
  );
  const [dischargeInstructionsInput, setDischargeInstructionsInput] = useState('');
  const [followUpInstructionsInput, setFollowUpInstructionsInput] = useState('');
  const [followUpDateInput, setFollowUpDateInput] = useState('');
  const [warningSignsInput, setWarningSignsInput] = useState('');
  const [completionNoteInput, setCompletionNoteInput] = useState('');
  const [confirmNoPrescriptionInput, setConfirmNoPrescriptionInput] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  if (syncKey !== lastSyncKey) {
    setLastSyncKey(syncKey);
    setClinicalConclusionInput(clinicalContext.clinicalConclusion ?? '');
    setDischargeInstructionsInput('');
    setFollowUpInstructionsInput('');
    setFollowUpDateInput('');
    setWarningSignsInput('');
    setCompletionNoteInput('');
    setConfirmNoPrescriptionInput(false);
    setValidationError(null);
  }

  const clearErrors = () => {
    if (validationError) {
      setValidationError(null);
    }
    if (submitError && onClearSubmitError) {
      onClearSubmitError();
    }
  };

  const primaryDiagnosis = clinicalContext.diagnoses.find(
    (diag) => diag.isPrimary && diag.code.trim().length > 0,
  );
  const isSessionInProgress =
    isFinalized ||
    (workspaceState === 'IN_PROGRESS' && clinicalContext.medicalRecordStatus === 'DRAFT');
  const hasPrimaryDiagnosis = Boolean(primaryDiagnosis);
  const hasNoPendingDiagnostics =
    clinicalContext.diagnosticSummary.openQueueEntries === 0 &&
    clinicalContext.diagnosticSummary.allMandatoryResultsReady &&
    !(clinicalContext.diagnosticOrders ?? []).some(
      (order) => order.status !== 'COMPLETED' && order.status !== 'CANCELLED',
    );
  const hasClinicalConclusion = Boolean(
    clinicalConclusionInput.trim() ||
    clinicalContext.clinicalConclusion?.trim() ||
    primaryDiagnosis?.clinicalDiagnosis?.trim(),
  );
  const isPrescriptionValid = !(
    (clinicalContext.prescriptionSummary.latestPrescriptionStatus === 'DRAFT' ||
      clinicalContext.prescriptionSummary.latestPrescriptionStatus === 'PENDING') &&
    clinicalContext.prescriptionSummary.itemCount === 0
  );

  const readinessItems = [
    {
      id: 'session',
      label: 'Phiên khám & Bệnh án nháp',
      detail: isFinalized
        ? `Đã khóa hoàn tất (Phiên bản #${clinicalContext.currentRevisionNo ?? 1})`
        : isSessionInProgress
          ? `Đang khám (IN_PROGRESS) • Bản nháp #${clinicalContext.currentRevisionNo ?? 1}`
          : `Trạng thái hiện tại: ${workspaceState}`,
      ready: isSessionInProgress,
    },
    {
      id: 'diagnosis',
      label: 'Chẩn đoán chính (ICD-10)',
      detail: primaryDiagnosis
        ? `Đã ghi nhận: ${primaryDiagnosis.code} — ${primaryDiagnosis.name}`
        : 'Chưa có chẩn đoán ICD-10 chính',
      ready: hasPrimaryDiagnosis,
    },
    {
      id: 'diagnostics',
      label: 'Kết quả Cận lâm sàng (CLS)',
      detail: hasNoPendingDiagnostics
        ? `Không còn chỉ định chờ (${clinicalContext.diagnosticSummary.completedQueueEntries}/${clinicalContext.diagnosticSummary.totalQueueEntries} hoàn tất)`
        : `Còn ${clinicalContext.diagnosticSummary.openQueueEntries} chỉ định CLS chưa hoàn tất`,
      ready: hasNoPendingDiagnostics,
    },
    {
      id: 'conclusion',
      label: 'Kết luận lâm sàng',
      detail: hasClinicalConclusion
        ? 'Đã có kết luận lâm sàng'
        : 'Cần bổ sung kết luận lâm sàng trước khi khóa bệnh án',
      ready: hasClinicalConclusion,
    },
  ];

  const allChecklistReady =
    isSessionInProgress &&
    hasPrimaryDiagnosis &&
    hasNoPendingDiagnostics &&
    hasClinicalConclusion &&
    isPrescriptionValid;

  const handleResetDraft = () => {
    clearErrors();
    setClinicalConclusionInput(clinicalContext.clinicalConclusion ?? '');
    setDischargeInstructionsInput('');
    setFollowUpInstructionsInput('');
    setFollowUpDateInput('');
    setWarningSignsInput('');
    setCompletionNoteInput('');
    setConfirmNoPrescriptionInput(false);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setValidationError(null);
    onClearSubmitError?.();

    const effectiveRevisionNo =
      expectedRevisionNo ??
      (clinicalContext.currentRevisionNo !== null &&
      clinicalContext.currentRevisionNo !== undefined &&
      clinicalContext.currentRevisionNo >= 1
        ? clinicalContext.currentRevisionNo
        : null);

    const candidatePayload: CompleteVisitRequest = {
      clinicalConclusion: clinicalConclusionInput.trim() || null,
      dischargeInstructions: dischargeInstructionsInput.trim() || null,
      followUpInstructions: followUpInstructionsInput.trim() || null,
      followUpDate: followUpDateInput.trim() || null,
      warningSigns: warningSignsInput.trim() || null,
      completionNote: completionNoteInput.trim() || null,
      notes: completionNoteInput.trim() || null,
      confirmNoPrescription: confirmNoPrescriptionInput,
      expectedStateVersion: expectedStateVersion ?? null,
      expectedRevisionNo: effectiveRevisionNo,
    };

    const parsed = completeVisitSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(firstIssue?.message ?? 'Thông tin hoàn tất lượt khám không hợp lệ.');
      return;
    }

    if (!hasPrimaryDiagnosis) {
      setValidationError(
        'Vui lòng ghi nhận ít nhất một chẩn đoán chính (ICD-10) trước khi hoàn tất lượt khám.',
      );
      return;
    }

    if (!hasNoPendingDiagnostics) {
      setValidationError(
        'Chưa thể hoàn tất lượt khám khi còn phiếu chỉ định hoặc hàng đợi cận lâm sàng chưa hoàn tất.',
      );
      return;
    }

    if (!hasClinicalConclusion) {
      setValidationError('Vui lòng nhập kết luận lâm sàng trước khi hoàn tất lượt khám.');
      return;
    }

    if (!isPrescriptionValid) {
      setValidationError(
        'Đơn thuốc nháp hiện chưa có thuốc kê đơn. Vui lòng bổ sung thuốc hoặc hủy đơn nháp trước khi hoàn tất.',
      );
      return;
    }

    await onSubmitCompleteVisit?.(candidatePayload);
  };

  return (
    <Card>
      <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <FileCheck2 className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h3 className="text-sm font-bold text-navy">
              Hoàn tất lượt khám & Khóa hồ sơ bệnh án (UC38)
            </h3>
            <p className="text-[11px] text-sub">
              Kiểm tra điều kiện hoàn tất, ghi nhận dặn dò xuất viện / hẹn tái khám và khóa bệnh án
              (FINALIZED)
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={isFinalized ? 'emerald' : allChecklistReady ? 'sky' : 'amber'}>
            {isFinalized
              ? 'Đã hoàn tất (FINALIZED)'
              : allChecklistReady
                ? 'Đủ điều kiện hoàn tất'
                : 'Chưa đủ điều kiện hoàn tất'}
          </Badge>

          {!hasWritePermission ? (
            <Badge tone="amber">Khóa quyền MEDICAL_RECORD_WRITE</Badge>
          ) : isFinalized ? (
            <Badge tone="emerald">
              <ShieldCheck className="h-3 w-3 mr-1 inline" aria-hidden="true" />
              Hồ sơ đã khóa
            </Badge>
          ) : isActionable ? (
            <Button
              type="button"
              variant={isFormOpen ? 'secondary' : 'primary'}
              size="sm"
              aria-expanded={isFormOpen}
              onClick={() => {
                clearErrors();
                setFormOpen(!isFormOpen);
              }}
            >
              <ClipboardCheck className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{isFormOpen ? 'Thu gọn biểu mẫu UC38' : 'Hoàn tất lượt khám (UC38)'}</span>
            </Button>
          ) : (
            <Badge tone="slate">
              <Lock className="h-3 w-3 mr-1 inline" aria-hidden="true" />
              Tạm khóa hoàn tất
            </Badge>
          )}
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        {isFinalized && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50/70 p-4 space-y-2 text-xs">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="font-bold text-emerald-900 flex items-center gap-1.5">
                <CheckCircle2 className="h-4 w-4 text-emerald-600" aria-hidden="true" />
                <span>Hồ sơ bệnh án đã khóa hoàn tất (FINALIZED)</span>
              </div>
              <span className="font-mono text-[11px] font-semibold text-emerald-800">
                Hoàn tất lúc: {visit.completedAt ?? clinicalContext.finalizedAt ?? '—'}
              </span>
            </div>
            <div className="text-emerald-800">
              Kết luận lâm sàng:{' '}
              <strong className="text-navy">
                {clinicalContext.clinicalConclusion ?? 'Đã xác nhận kết luận lâm sàng'}
              </strong>
            </div>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-sub">
              <span>
                Chẩn đoán xác định:{' '}
                <strong className="text-ink">
                  {primaryDiagnosis
                    ? `${primaryDiagnosis.code} — ${primaryDiagnosis.name}`
                    : 'Đã xác nhận'}
                </strong>
              </span>
              <span>
                Đơn thuốc ngoại trú:{' '}
                <strong className="font-mono text-ink">
                  {clinicalContext.prescriptionSummary.latestPrescriptionCode
                    ? `${clinicalContext.prescriptionSummary.latestPrescriptionCode} (${clinicalContext.prescriptionSummary.latestPrescriptionStatus ?? 'ISSUED'})`
                    : 'Không kê đơn'}
                </strong>
              </span>
            </div>
          </div>
        )}

        {!isFinalized && !isActionable && (disabledReason || !hasWritePermission) && (
          <div className="rounded-xl border border-amber-200 bg-amber-50/70 px-3.5 py-2.5 text-xs text-amber-900 flex items-center gap-2">
            <Lock className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
            <span>
              {!hasWritePermission
                ? 'Tài khoản chưa được cấp quyền ghi nhận hồ sơ bệnh án (MEDICAL_RECORD_WRITE).'
                : disabledReason}
            </span>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 text-xs">
          {readinessItems.map((item) => (
            <div
              key={item.id}
              className="rounded-xl bg-canvas border border-border p-3 flex items-start justify-between gap-2"
            >
              <div className="space-y-0.5 min-w-0">
                <div className="font-bold text-navy">{item.label}</div>
                <div className="text-[11px] text-sub truncate">{item.detail}</div>
              </div>
              <Badge tone={item.ready ? 'emerald' : 'amber'}>
                {item.ready ? 'Đạt' : 'Chờ xử lý'}
              </Badge>
            </div>
          ))}
        </div>

        {isActionable && isFormOpen && (
          <form
            onSubmit={(e) => void handleSubmit(e)}
            aria-label="Biểu mẫu hoàn tất lượt khám"
            className="rounded-2xl border border-primary/25 bg-canvas/60 p-4 space-y-4"
          >
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border pb-3">
              <div>
                <h4 className="text-xs font-bold text-navy">
                  Xác nhận kết luận lâm sàng & Hướng dẫn xuất viện (UC38)
                </h4>
                <p className="text-[11px] text-sub">
                  Sau khi xác nhận hoàn tất, hồ sơ bệnh án chuyển sang{' '}
                  <span className="font-mono font-semibold text-navy">FINALIZED</span> và không thể
                  chỉnh sửa trực tiếp.
                </p>
              </div>
              <span className="font-mono text-[11px] font-semibold text-sub">
                state_version: v{expectedStateVersion ?? 1} • revision: #
                {expectedRevisionNo ?? clinicalContext.currentRevisionNo ?? 1}
              </span>
            </div>

            {(validationError || submitError) && (
              <div role="alert">
                <Alert tone="error">
                  <div className="flex items-center gap-2">
                    <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>{validationError ?? submitError}</span>
                  </div>
                </Alert>
              </div>
            )}

            <div className="grid grid-cols-1 md:grid-cols-2 gap-3.5 text-xs">
              <div className="md:col-span-2 space-y-1">
                <label
                  htmlFor="uc38-clinical-conclusion"
                  className="block text-[11px] font-bold text-navy"
                >
                  Kết luận lâm sàng cuối cùng
                </label>
                <textarea
                  id="uc38-clinical-conclusion"
                  aria-label="Kết luận lâm sàng cuối cùng"
                  rows={2}
                  value={clinicalConclusionInput}
                  onChange={(e) => {
                    clearErrors();
                    setClinicalConclusionInput(e.target.value);
                  }}
                  placeholder="Nhập kết luận lâm sàng xác định cho lượt khám (tối đa 2000 ký tự)..."
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="uc38-discharge-instructions"
                  className="block text-[11px] font-bold text-navy"
                >
                  Hướng dẫn điều trị / dặn dò xuất viện
                </label>
                <textarea
                  id="uc38-discharge-instructions"
                  aria-label="Hướng dẫn điều trị / dặn dò xuất viện"
                  rows={2}
                  value={dischargeInstructionsInput}
                  onChange={(e) => {
                    clearErrors();
                    setDischargeInstructionsInput(e.target.value);
                  }}
                  placeholder="Chế độ dùng thuốc, dinh dưỡng, sinh hoạt tại nhà..."
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="uc38-follow-up-instructions"
                  className="block text-[11px] font-bold text-navy"
                >
                  Hướng dẫn tái khám
                </label>
                <textarea
                  id="uc38-follow-up-instructions"
                  aria-label="Hướng dẫn tái khám"
                  rows={2}
                  value={followUpInstructionsInput}
                  onChange={(e) => {
                    clearErrors();
                    setFollowUpInstructionsInput(e.target.value);
                  }}
                  placeholder="Nội dung cần chuẩn bị hoặc xét nghiệm khi tái khám..."
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="uc38-follow-up-date"
                  className="block text-[11px] font-bold text-navy"
                >
                  <CalendarCheck
                    className="h-3.5 w-3.5 inline mr-1 text-primary"
                    aria-hidden="true"
                  />
                  Ngày hẹn tái khám
                </label>
                <input
                  id="uc38-follow-up-date"
                  aria-label="Ngày hẹn tái khám"
                  type="date"
                  value={followUpDateInput}
                  onChange={(e) => {
                    clearErrors();
                    setFollowUpDateInput(e.target.value);
                  }}
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="uc38-warning-signs"
                  className="block text-[11px] font-bold text-navy"
                >
                  Dấu hiệu cảnh báo cần tái khám ngay
                </label>
                <input
                  id="uc38-warning-signs"
                  aria-label="Dấu hiệu cảnh báo cần tái khám ngay"
                  type="text"
                  value={warningSignsInput}
                  onChange={(e) => {
                    clearErrors();
                    setWarningSignsInput(e.target.value);
                  }}
                  placeholder="VD: Đau ngực tăng, khó thở, sốt cao không hạ..."
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>

              <div className="md:col-span-2 space-y-1">
                <label
                  htmlFor="uc38-completion-note"
                  className="block text-[11px] font-bold text-navy"
                >
                  Ghi chú hoàn tất lượt khám
                </label>
                <input
                  id="uc38-completion-note"
                  aria-label="Ghi chú hoàn tất lượt khám"
                  type="text"
                  value={completionNoteInput}
                  onChange={(e) => {
                    clearErrors();
                    setCompletionNoteInput(e.target.value);
                  }}
                  placeholder="Ghi chú tổng kết phiên khám hoặc lý do cập nhật bản chốt (tối đa 500 ký tự)..."
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                />
              </div>
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pt-2 border-t border-border">
              <label className="inline-flex items-center gap-2 text-xs text-ink cursor-pointer">
                <input
                  type="checkbox"
                  aria-label="Xác nhận hoàn tất không kê đơn thuốc ngoại trú"
                  checked={confirmNoPrescriptionInput}
                  onChange={(e) => {
                    clearErrors();
                    setConfirmNoPrescriptionInput(e.target.checked);
                  }}
                  className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                />
                <span>Xác nhận hoàn tất không kê đơn thuốc ngoại trú (nếu không dùng thuốc)</span>
              </label>

              <div className="flex flex-wrap items-center gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  disabled={isSubmitting}
                  onClick={handleResetDraft}
                >
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Đặt lại</span>
                </Button>
                <Button
                  type="submit"
                  variant="primary"
                  size="sm"
                  loading={isSubmitting}
                  disabled={isSubmitting}
                >
                  <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Xác nhận hoàn tất & Khóa bệnh án (UC38)</span>
                </Button>
              </div>
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
