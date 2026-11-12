import {
  AlertTriangle,
  CheckCircle2,
  ClipboardCheck,
  FileSignature,
  FileText,
  History,
  Layers,
  Lock,
  PencilLine,
  SendHorizonal,
  ShieldAlert,
  Stethoscope,
  XCircle,
} from 'lucide-react';
import { useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import {
  enterDiagnosticResultSchema,
  updateDiagnosticResultSchema,
} from '../schemas/diagnostic-result-entry.schema';
import type {
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticResultEntryItemDto,
  DiagnosticResultHistoryItemDto,
  DiagnosticResultVersionItemDto,
  EnterDiagnosticResultRequest,
  UpdateDiagnosticResultRequest,
} from '../types/diagnostic.types';

interface DiagnosticResultEntryDetailPanelProps {
  order: DiagnosticResultEntryItemDto | null;
  clinicalContext?: DiagnosticOrderClinicalSummaryDto | null;
  versions?: DiagnosticResultVersionItemDto[];
  statusHistory?: DiagnosticResultHistoryItemDto[];
  hasResultWritePermission: boolean;
  isSubmitting: boolean;
  onEnterResult: (orderCode: string, payload: EnterDiagnosticResultRequest) => void;
  onUpdateResult: (orderCode: string, payload: UpdateDiagnosticResultRequest) => void;
}

const RESULT_ENTRY_ELIGIBILITY_CRITERIA: Array<{
  key: keyof DiagnosticResultEntryItemDto['eligibility'];
  label: string;
}> = [
  {
    key: 'isOrderExecutionStartedOrCompleted',
    label: 'Phiếu chỉ định đã bắt đầu / hoàn tất thực hiện kỹ thuật (IN_PROGRESS — UC42)',
  },
  {
    key: 'isVisitEligible',
    label: 'Lượt khám lâm sàng đang hoạt động hợp lệ',
  },
  {
    key: 'isMedicalRecordOpen',
    label: 'Hồ sơ bệnh án của lượt khám đang mở (chưa đóng)',
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
    key: 'hasResultWritePermission',
    label: 'Nhân viên có quyền nhập & trả kết quả (DIAGNOSTIC_RESULT_WRITE)',
  },
];

const DRAFT_STATUS_OPTIONS: Array<{ value: string; label: string; hint: string }> = [
  {
    value: 'DRAFT',
    label: 'DRAFT — Bản nháp',
    hint: 'Lưu tạm, tiếp tục hiệu chỉnh sau',
  },
  {
    value: 'PRELIMINARY',
    label: 'PRELIMINARY — Kết quả sơ bộ',
    hint: 'Công bố kết quả tạm cho bác sĩ tham khảo',
  },
  {
    value: 'PENDING_VERIFY',
    label: 'PENDING_VERIFY — Chờ thẩm định',
    hint: 'Chờ nhân viên/bác sĩ có thẩm quyền duyệt',
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

function resultStatusMeta(resultStatus?: string | null): {
  label: string;
  tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose';
} {
  switch ((resultStatus ?? 'NONE').toUpperCase()) {
    case 'DRAFT':
      return { label: 'Bản nháp (DRAFT)', tone: 'sky' };
    case 'PRELIMINARY':
      return { label: 'Kết quả sơ bộ (PRELIMINARY)', tone: 'sky' };
    case 'PENDING_VERIFY':
      return { label: 'Chờ thẩm định (PENDING_VERIFY)', tone: 'amber' };
    case 'FINALIZED':
      return { label: 'Đã duyệt (FINALIZED)', tone: 'emerald' };
    case 'VERIFIED':
      return { label: 'Đã thẩm định (VERIFIED)', tone: 'emerald' };
    default:
      return { label: 'Chưa có kết quả', tone: 'amber' };
  }
}

interface DiagnosticResultEntryFormProps {
  order: DiagnosticResultEntryItemDto;
  canSubmitResult: boolean;
  canFinalizeResult: boolean;
  isSubmitting: boolean;
  onEnterResult: (orderCode: string, payload: EnterDiagnosticResultRequest) => void;
}

function DiagnosticResultEntryForm({
  order,
  canSubmitResult,
  canFinalizeResult,
  isSubmitting,
  onEnterResult,
}: DiagnosticResultEntryFormProps) {
  const hasExistingDraft = Boolean(
    order.hasResult &&
    order.latestVersionNo != null &&
    order.latestVersionNo > 0 &&
    !order.isResultFinalized,
  );

  const [resultText, setResultText] = useState(order.resultText ?? '');
  const [conclusion, setConclusion] = useState(order.conclusion ?? '');
  const [reportFileUrl, setReportFileUrl] = useState(order.reportFileUrl ?? '');
  const [draftStatus, setDraftStatus] = useState<string>(
    order.resultStatus === 'PRELIMINARY' || order.resultStatus === 'PENDING_VERIFY'
      ? order.resultStatus
      : 'DRAFT',
  );
  const [correctionReason, setCorrectionReason] = useState(order.correctionReason ?? '');
  const [notes, setNotes] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleSubmit = (isFinalized: boolean) => {
    setValidationError(null);

    const candidatePayload = {
      resultText,
      conclusion: conclusion.trim() ? conclusion : null,
      reportFileUrl: reportFileUrl.trim() ? reportFileUrl : null,
      status: draftStatus,
      isFinalized,
      isVerified: isFinalized ? order.requiresResultVerification : false,
      correctionReason: correctionReason.trim() ? correctionReason : null,
      notes: notes.trim() ? notes : null,
      expectedStateVersion: order.stateVersion,
    };

    const parsed = enterDiagnosticResultSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(
        firstIssue?.message ?? 'Dữ liệu kết quả cận lâm sàng không hợp lệ. Vui lòng kiểm tra lại.',
      );
      return;
    }

    onEnterResult(order.orderCode, {
      orderId: order.orderId,
      resultText: parsed.data.resultText,
      conclusion: parsed.data.conclusion ?? null,
      reportFileUrl: parsed.data.reportFileUrl ?? null,
      status: isFinalized ? null : parsed.data.status,
      isFinalized: parsed.data.isFinalized,
      isVerified: parsed.data.isVerified ?? false,
      correctionReason: parsed.data.correctionReason ?? null,
      notes: parsed.data.notes ?? null,
      expectedStateVersion: parsed.data.expectedStateVersion ?? order.stateVersion,
    });
  };

  return (
    <div
      className="space-y-3 rounded-xl border border-border bg-canvas p-3.5"
      data-testid="result-entry-form"
    >
      <div className="font-bold text-navy">
        Nhập / hiệu chỉnh kết quả cận lâm sàng (UC43)
        {hasExistingDraft && (
          <span className="ml-2 font-mono text-[10px] font-semibold text-sub">
            đang sửa bản v{order.latestVersionNo}
          </span>
        )}
      </div>

      {validationError && (
        <Alert tone="error">
          <span data-testid="result-form-validation-error">{validationError}</span>
        </Alert>
      )}

      <div>
        <label htmlFor="result-text" className="block text-[11px] font-semibold text-navy">
          Nội dung kết quả cận lâm sàng <span className="text-rose-600">*</span> (tối đa 8000 ký tự)
        </label>
        <textarea
          id="result-text"
          aria-label="Nội dung kết quả cận lâm sàng"
          rows={5}
          value={resultText}
          disabled={!canSubmitResult || isSubmitting}
          onChange={(e) => setResultText(e.target.value)}
          placeholder="Mô tả chi tiết kết quả: chỉ số xét nghiệm, hình thái chụp chiếu, đường điện tim..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label htmlFor="result-conclusion" className="block text-[11px] font-semibold text-navy">
          Kết luận chẩn đoán cận lâm sàng{' '}
          <span className="font-normal text-sub">
            (bắt buộc khi duyệt &amp; trả kết quả — tối đa 2000 ký tự)
          </span>
        </label>
        <textarea
          id="result-conclusion"
          aria-label="Kết luận chẩn đoán cận lâm sàng"
          rows={3}
          value={conclusion}
          disabled={!canSubmitResult || isSubmitting}
          onChange={(e) => setConclusion(e.target.value)}
          placeholder="Kết luận tổng hợp gửi bác sĩ chỉ định..."
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label htmlFor="result-file-url" className="block text-[11px] font-semibold text-navy">
          Đường dẫn tệp kết quả đính kèm (ReportFileUrl — tùy chọn)
        </label>
        <input
          id="result-file-url"
          type="url"
          aria-label="Đường dẫn tệp kết quả đính kèm"
          value={reportFileUrl}
          disabled={!canSubmitResult || isSubmitting}
          onChange={(e) => setReportFileUrl(e.target.value)}
          placeholder="https://pacs.medflow.vn/report/CLS-2026-xxxx.pdf"
          className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 font-mono text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <fieldset className="space-y-1.5">
        <legend className="text-[11px] font-semibold text-navy">
          Trạng thái khi lưu (không duyệt)
        </legend>
        <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-3">
          {DRAFT_STATUS_OPTIONS.map((option) => (
            <label
              key={option.value}
              className={`flex cursor-pointer flex-col gap-0.5 rounded-lg border px-2.5 py-2 text-[11px] transition-colors ${
                draftStatus === option.value
                  ? 'border-primary bg-sky-50/80 text-navy'
                  : 'border-border bg-white text-sub hover:border-primary/50'
              } ${!canSubmitResult || isSubmitting ? 'cursor-not-allowed opacity-60' : ''}`}
            >
              <span className="flex items-center gap-1.5 font-semibold">
                <input
                  type="radio"
                  name="result-draft-status"
                  value={option.value}
                  checked={draftStatus === option.value}
                  disabled={!canSubmitResult || isSubmitting}
                  onChange={() => setDraftStatus(option.value)}
                  className="h-3.5 w-3.5 border-border text-primary focus:ring-primary"
                />
                {option.label}
              </span>
              <span className="pl-5 text-[10px] text-sub">{option.hint}</span>
            </label>
          ))}
        </div>
      </fieldset>

      {hasExistingDraft && (
        <div>
          <label
            htmlFor="result-correction-reason"
            className="block text-[11px] font-semibold text-navy"
          >
            Lý do chỉnh sửa bản kết quả hiện có (tối thiểu 10 ký tự)
          </label>
          <input
            id="result-correction-reason"
            type="text"
            aria-label="Lý do chỉnh sửa bản kết quả hiện có"
            value={correctionReason}
            disabled={!canSubmitResult || isSubmitting}
            onChange={(e) => setCorrectionReason(e.target.value)}
            placeholder="Vd: bổ sung giá trị HbA1c sau khi chạy lại mẫu..."
            className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
          />
        </div>
      )}

      <div>
        <label htmlFor="result-notes" className="block text-[11px] font-semibold text-navy">
          Ghi chú nội bộ khi nhập kết quả (tùy chọn — tối đa 2000 ký tự)
        </label>
        <input
          id="result-notes"
          type="text"
          aria-label="Ghi chú nội bộ khi nhập kết quả"
          value={notes}
          disabled={!canSubmitResult || isSubmitting}
          onChange={(e) => setNotes(e.target.value)}
          placeholder="Ghi chú kỹ thuật, mẫu chạy lại, lưu ý cho người thẩm định..."
          className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div className="grid grid-cols-1 gap-2 pt-1 sm:grid-cols-2">
        <Button
          type="button"
          variant="secondary"
          loading={isSubmitting}
          disabled={!canSubmitResult || isSubmitting}
          onClick={() => handleSubmit(false)}
        >
          <FileSignature className="h-4 w-4" aria-hidden="true" />
          <span>Lưu kết quả ({draftStatus})</span>
        </Button>
        <Button
          type="button"
          loading={isSubmitting}
          disabled={!canSubmitResult || !canFinalizeResult || isSubmitting}
          onClick={() => handleSubmit(true)}
        >
          <SendHorizonal className="h-4 w-4" aria-hidden="true" />
          <span>Duyệt &amp; trả kết quả</span>
        </Button>
      </div>

      <p className="text-[10px] leading-relaxed text-sub">
        Phiên bản trạng thái hiện tại: <span className="font-mono">v{order.stateVersion}</span> •
        Mọi lượt lưu tạo phiên bản kết quả mới. Sau khi duyệt, phiếu khóa nhập mới; sửa nội dung đã
        khóa ngay trên phiếu này khi còn đủ điều kiện.
      </p>
    </div>
  );
}

interface DiagnosticResultAmendFormProps {
  order: DiagnosticResultEntryItemDto;
  isSubmitting: boolean;
  onUpdateResult: (orderCode: string, payload: UpdateDiagnosticResultRequest) => void;
}

function DiagnosticResultAmendForm({
  order,
  isSubmitting,
  onUpdateResult,
}: DiagnosticResultAmendFormProps) {
  const [resultText, setResultText] = useState(order.resultText ?? '');
  const [conclusion, setConclusion] = useState(order.conclusion ?? '');
  const [reportFileUrl, setReportFileUrl] = useState(order.reportFileUrl ?? '');
  const [reportFileUrlTouched, setReportFileUrlTouched] = useState(false);
  const [correctionReason, setCorrectionReason] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleSubmit = () => {
    setValidationError(null);

    const candidatePayload = {
      resultText,
      conclusion,
      reportFileUrl: reportFileUrlTouched ? reportFileUrl.trim() : undefined,
      correctionReason,
      expectedStateVersion: order.stateVersion,
    };

    const parsed = updateDiagnosticResultSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(
        firstIssue?.message ??
          'Dữ liệu sửa kết quả cận lâm sàng không hợp lệ. Vui lòng kiểm tra lại.',
      );
      return;
    }

    onUpdateResult(order.orderCode, {
      orderId: order.orderId,
      resultText: parsed.data.resultText,
      conclusion: parsed.data.conclusion,
      reportFileUrl: reportFileUrlTouched ? (parsed.data.reportFileUrl ?? '') : null,
      correctionReason: parsed.data.correctionReason,
      expectedStateVersion: parsed.data.expectedStateVersion,
    });
  };

  return (
    <div
      className="space-y-3 rounded-xl border border-border bg-canvas p-3.5"
      data-testid="result-amend-form"
    >
      <div className="font-bold text-navy">Sửa kết quả cận lâm sàng đã khóa</div>

      {validationError && (
        <Alert tone="error">
          <span data-testid="result-form-validation-error">{validationError}</span>
        </Alert>
      )}

      <div>
        <label htmlFor="amend-result-text" className="block text-[11px] font-semibold text-navy">
          Nội dung kết quả cận lâm sàng <span className="text-rose-600">*</span> (3–5000 ký tự)
        </label>
        <textarea
          id="amend-result-text"
          aria-label="Nội dung kết quả cận lâm sàng"
          rows={5}
          value={resultText}
          disabled={isSubmitting}
          onChange={(event) => setResultText(event.target.value)}
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label
          htmlFor="amend-result-conclusion"
          className="block text-[11px] font-semibold text-navy"
        >
          Kết luận chẩn đoán cận lâm sàng <span className="text-rose-600">*</span> (3–2000 ký tự)
        </label>
        <textarea
          id="amend-result-conclusion"
          aria-label="Kết luận chẩn đoán cận lâm sàng"
          rows={3}
          value={conclusion}
          disabled={isSubmitting}
          onChange={(event) => setConclusion(event.target.value)}
          className="mt-1 w-full rounded-lg border border-border bg-white p-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label
          htmlFor="amend-result-file-url"
          className="block text-[11px] font-semibold text-navy"
        >
          Đường dẫn tệp kết quả đính kèm
          <span className="font-normal text-sub"> (để nguyên = giữ file; xóa trắng = gỡ file)</span>
        </label>
        <input
          id="amend-result-file-url"
          type="url"
          aria-label="Đường dẫn tệp kết quả đính kèm"
          value={reportFileUrl}
          disabled={isSubmitting}
          onChange={(event) => {
            setReportFileUrlTouched(true);
            setReportFileUrl(event.target.value);
          }}
          placeholder="https://pacs.medflow.vn/report/CLS-2026-xxxx.pdf"
          className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 font-mono text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <div>
        <label
          htmlFor="amend-correction-reason"
          className="block text-[11px] font-semibold text-navy"
        >
          Lý do sửa kết quả đã khóa <span className="text-rose-600">*</span> (5–500 ký tự)
        </label>
        <input
          id="amend-correction-reason"
          type="text"
          aria-label="Lý do sửa kết quả đã khóa"
          value={correctionReason}
          disabled={isSubmitting}
          onChange={(event) => setCorrectionReason(event.target.value)}
          placeholder="Vd: đính chính số tiểu cầu sau khi chạy lại mẫu..."
          className="mt-1 h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:opacity-60"
        />
      </div>

      <Button type="button" loading={isSubmitting} disabled={isSubmitting} onClick={handleSubmit}>
        <PencilLine className="h-4 w-4" aria-hidden="true" />
        <span>Sửa kết quả đã khóa</span>
      </Button>

      <p className="text-[10px] leading-relaxed text-sub">
        Phiên bản trạng thái hiện tại: <span className="font-mono">v{order.stateVersion}</span> •
        Lưu tạo phiên bản kết quả mới và tăng state version. Phiếu và lượt xếp hàng vẫn hoàn tất.
      </p>
    </div>
  );
}

export function DiagnosticResultEntryDetailPanel({
  order,
  clinicalContext,
  versions,
  statusHistory,
  hasResultWritePermission,
  isSubmitting,
  onEnterResult,
  onUpdateResult,
}: DiagnosticResultEntryDetailPanelProps) {
  if (!order) {
    return (
      <Card>
        <CardContent className="py-12 text-center">
          <ClipboardCheck className="mx-auto mb-2 h-8 w-8 text-sub" aria-hidden="true" />
          <div className="text-sm font-semibold text-navy">
            Chọn một phiếu chỉ định cận lâm sàng ở bảng bên trái
          </div>
          <p className="mt-1 text-xs text-sub">
            Hệ thống sẽ hiển thị đầy đủ thông tin hành chính, bối cảnh lâm sàng, checklist điều
            kiện, lịch sử phiên bản kết quả và biểu mẫu nhập &amp; trả kết quả (UC43).
          </p>
        </CardContent>
      </Card>
    );
  }

  const normalizedOrderStatus = (order.orderStatus || order.status || '').toUpperCase();
  const resultMeta = resultStatusMeta(order.resultStatus);
  const isFinalized = order.isResultFinalized || order.eligibility.isResultAlreadyFinalized;
  const canSubmitResult =
    !isFinalized && order.canEnterResult && hasResultWritePermission && !isSubmitting;
  const canFinalizeResult =
    !isFinalized && order.canFinalizeResult && hasResultWritePermission && !isSubmitting;
  const amendDisabledReason = order.amendDisabledReason ?? order.eligibility.amendDisabledReason;
  const showAmendForm = isFinalized && order.canAmendResult && hasResultWritePermission;
  const versionList = versions && versions.length > 0 ? versions : (order.versions ?? []);
  const sortedVersions = [...versionList].sort((a, b) => b.versionNo - a.versionNo);
  const historyList = statusHistory ?? [];

  return (
    <Card data-testid="diagnostic-result-entry-detail-panel">
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
            <Badge tone={resultMeta.tone}>{resultMeta.label}</Badge>
            <Badge
              tone={
                normalizedOrderStatus === 'IN_PROGRESS'
                  ? 'sky'
                  : normalizedOrderStatus === 'COMPLETED'
                    ? 'emerald'
                    : 'slate'
              }
            >
              Phiếu: {normalizedOrderStatus || '—'}
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
              <ClipboardCheck className="h-4 w-4 text-primary" aria-hidden="true" />
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
                {clinicalContext.clinicalDiagnosis && (
                  <div>
                    <span className="text-sub">Chẩn đoán lâm sàng: </span>
                    <span className="text-ink">{clinicalContext.clinicalDiagnosis}</span>
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

        {/* 3. Finalized lock banner */}
        {isFinalized && (
          <Alert tone="warning">
            <div className="flex items-start gap-2" data-testid="result-finalized-lock-banner">
              <Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">
                  Kết quả đã được duyệt &amp; trả ({resultMeta.label}) — phiếu đang khóa
                </div>
                <div className="mt-0.5 text-xs">
                  Duyệt bởi{' '}
                  <span className="font-semibold">
                    {order.finalizedByStaffName ?? 'nhân viên cận lâm sàng'}
                  </span>{' '}
                  lúc <span className="font-mono">{formatDateTime(order.resultFinalizedAt)}</span>.
                  {order.canAmendResult
                    ? ' Có thể sửa nội dung đã khóa. Phiếu và lượt xếp hàng vẫn hoàn tất.'
                    : ' Không thể nhập mới.'}
                </div>
              </div>
            </div>
          </Alert>
        )}

        {/* 4. Eligibility Checklist */}
        <section
          aria-label="Kiểm tra điều kiện nhập kết quả cận lâm sàng"
          className="rounded-xl border border-border bg-white p-3.5 space-y-2.5"
          data-testid="result-entry-eligibility-checklist"
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Checklist điều kiện nhập kết quả (UC43)</span>
            </div>
            <Badge tone={order.eligibility.isEligible ? 'emerald' : 'rose'}>
              {order.eligibility.isEligible ? 'Đạt tiêu chí nhập KQ' : 'Có tiêu chí chưa đạt'}
            </Badge>
          </div>

          <ul className="grid grid-cols-1 gap-1.5 text-[11px]">
            {RESULT_ENTRY_ELIGIBILITY_CRITERIA.map((criterion) => {
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

          {!order.canEnterResult && order.disabledReason && (
            <Alert tone="warning">
              <div
                className="flex items-start gap-2"
                data-testid="order-result-entry-disabled-reason"
              >
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <div>
                  <div className="font-bold">Không thể nhập kết quả cho phiếu này</div>
                  <div className="mt-0.5 text-xs">{order.disabledReason}</div>
                </div>
              </div>
            </Alert>
          )}
        </section>

        {/* 5. Version history */}
        {sortedVersions.length > 0 && (
          <section
            aria-label="Lịch sử phiên bản kết quả cận lâm sàng"
            className="rounded-xl border border-border bg-white p-3.5 space-y-2"
            data-testid="result-version-history"
          >
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <History className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Lịch sử phiên bản kết quả ({sortedVersions.length})</span>
            </div>
            <ul className="space-y-1.5">
              {sortedVersions.map((version) => (
                <li
                  key={version.versionId}
                  className="rounded-lg border border-border bg-canvas/70 px-2.5 py-2"
                  data-testid={`result-version-${version.versionNo}`}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono font-bold text-navy">v{version.versionNo}</span>
                    <Badge tone={version.isFinalized ? 'emerald' : 'sky'}>
                      {version.isFinalized ? 'Đã duyệt' : 'Chưa duyệt'}
                    </Badge>
                    <span className="text-[10px] text-sub">
                      {version.enteredByStaffName ?? '—'} • {formatDateTime(version.enteredAt)}
                    </span>
                  </div>
                  {version.resultText && (
                    <p className="mt-1 line-clamp-2 text-[11px] text-ink">{version.resultText}</p>
                  )}
                  {version.conclusion && (
                    <p className="mt-0.5 text-[11px] text-sub">
                      Kết luận: <span className="italic">{version.conclusion}</span>
                    </p>
                  )}
                  {version.correctionReason && (
                    <p className="mt-0.5 text-[10px] text-amber-700">
                      Lý do chỉnh sửa: {version.correctionReason}
                    </p>
                  )}
                  <p className="mt-0.5 font-mono text-[10px] text-sub">
                    {version.reportFileUrl
                      ? `Tệp: ${version.reportFileUrl}`
                      : 'Không có tệp đính kèm'}
                  </p>
                </li>
              ))}
            </ul>
          </section>
        )}

        {/* 6. Audit history */}
        {historyList.length > 0 && (
          <section
            aria-label="Nhật ký thao tác kết quả cận lâm sàng"
            className="rounded-xl border border-border bg-white p-3.5 space-y-1.5"
            data-testid="result-status-history"
          >
            <div className="flex items-center gap-1.5 font-bold text-navy">
              <History className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Nhật ký thao tác kết quả ({historyList.length})</span>
            </div>
            <ul className="space-y-1 text-[11px]">
              {historyList.map((entry) => (
                <li key={entry.auditLogId} className="flex flex-wrap items-center gap-1.5">
                  <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono font-semibold text-navy">
                    {entry.action}
                  </span>
                  <span className="text-sub">
                    {entry.resultStatus}
                    {entry.versionNo != null ? ` • v${entry.versionNo}` : ''} •{' '}
                    {entry.actorStaffName ?? '—'} • {formatDateTime(entry.occurredAtUtc)}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}

        {/* 7. RBAC Permission Notice */}
        {!hasResultWritePermission && (
          <Alert tone="error">
            <div
              className="flex items-start gap-2"
              data-testid="missing-result-write-permission-alert"
            >
              <Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">
                  Tài khoản chưa được cấp quyền nhập &amp; trả kết quả cận lâm sàng
                </div>
                <div className="mt-0.5 text-xs">
                  Bạn cần quyền <span className="font-mono">DIAGNOSTIC_RESULT_WRITE</span> để lưu
                  bản nháp / duyệt &amp; trả kết quả cho phiếu chỉ định này.
                </div>
              </div>
            </div>
          </Alert>
        )}

        {isFinalized && !order.canAmendResult && amendDisabledReason && (
          <Alert tone="warning">
            <div className="flex items-start gap-2" data-testid="result-amend-disabled-reason">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <div className="font-bold">Không thể sửa kết quả đã khóa</div>
                <div className="mt-0.5 text-xs">{amendDisabledReason}</div>
              </div>
            </div>
          </Alert>
        )}

        {/* 8. Entry or amend form */}
        {showAmendForm ? (
          <DiagnosticResultAmendForm
            key={`${order.orderCode}-amend-${order.latestVersionNo ?? 0}-${order.stateVersion}`}
            order={order}
            isSubmitting={isSubmitting}
            onUpdateResult={onUpdateResult}
          />
        ) : (
          !isFinalized && (
            <DiagnosticResultEntryForm
              key={`${order.orderCode}-${order.latestVersionNo ?? 0}-${order.stateVersion}`}
              order={order}
              canSubmitResult={canSubmitResult}
              canFinalizeResult={canFinalizeResult}
              isSubmitting={isSubmitting}
              onEnterResult={onEnterResult}
            />
          )
        )}
      </CardContent>
    </Card>
  );
}
