import {
  Activity,
  CheckCircle2,
  ClipboardList,
  Edit3,
  FileText,
  HeartPulse,
  Lock,
  RotateCcw,
  Save,
  ShieldAlert,
  Thermometer,
  Weight,
} from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import {
  calculateClinicalBmi,
  formatBloodPressureLabel,
  recordClinicalExaminationSchema,
} from '../schemas/consultation-workspace.schema';
import type {
  ConsultationClinicalContextDto,
  RecordClinicalExaminationRequest,
} from '../types/consultation-workspace.types';

export interface ConsultationVitalsAndClinicalCardProps {
  clinicalContext: ConsultationClinicalContextDto;
  canEditClinical: boolean;
  hasWritePermission?: boolean;
  readOnlyReason?: string | null;
  expectedStateVersion?: number | null;
  isSubmitting?: boolean;
  submitError?: string | null;
  isFormOpen?: boolean;
  onToggleForm?: (open: boolean) => void;
  onSubmitExamination?: (payload: RecordClinicalExaminationRequest) => Promise<void> | void;
  onClearSubmitError?: () => void;
}

const RECORD_STATUS_META: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' }
> = {
  NOT_CREATED: { label: 'Chưa khởi tạo bệnh án', tone: 'slate' },
  DRAFT: { label: 'Bệnh án nháp (DRAFT)', tone: 'amber' },
  FINALIZED: { label: 'Đã khóa bệnh án (FINALIZED)', tone: 'emerald' },
};

function parseOptionalNumber(raw: string): { value: number | null; invalid: boolean } {
  const trimmed = raw.trim();
  if (!trimmed) {
    return { value: null, invalid: false };
  }
  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) {
    return { value: null, invalid: true };
  }
  return { value: parsed, invalid: false };
}

function toInputString(val: number | null | undefined): string {
  return val !== null && val !== undefined ? String(val) : '';
}

export function ConsultationVitalsAndClinicalCard({
  clinicalContext,
  canEditClinical,
  hasWritePermission = true,
  readOnlyReason = null,
  expectedStateVersion = null,
  isSubmitting = false,
  submitError = null,
  isFormOpen: controlledIsFormOpen,
  onToggleForm,
  onSubmitExamination,
  onClearSubmitError,
}: ConsultationVitalsAndClinicalCardProps) {
  const { vitalSigns } = clinicalContext;
  const isEditable = canEditClinical && hasWritePermission;

  const [internalFormOpen, setInternalFormOpen] = useState(false);
  const isFormOpen = controlledIsFormOpen ?? internalFormOpen;

  const setFormOpen = (nextOpen: boolean) => {
    if (onToggleForm) {
      onToggleForm(nextOpen);
    } else {
      setInternalFormOpen(nextOpen);
    }
  };

  const contextSyncKey = `${clinicalContext.medicalRecordId ?? 'none'}:${clinicalContext.currentRevisionNo ?? 0}:${clinicalContext.lastRecordedAt ?? ''}`;
  const [lastSyncedKey, setLastSyncedKey] = useState(contextSyncKey);

  const [bpSystolicInput, setBpSystolicInput] = useState(() =>
    toInputString(vitalSigns.bpSystolicMmhg),
  );
  const [bpDiastolicInput, setBpDiastolicInput] = useState(() =>
    toInputString(vitalSigns.bpDiastolicMmhg),
  );
  const [pulseInput, setPulseInput] = useState(() => toInputString(vitalSigns.pulseBpm));
  const [temperatureInput, setTemperatureInput] = useState(() =>
    toInputString(vitalSigns.temperatureC),
  );
  const [spo2Input, setSpo2Input] = useState(() => toInputString(vitalSigns.spo2Percent));
  const [heightInput, setHeightInput] = useState(() => toInputString(vitalSigns.heightCm));
  const [weightInput, setWeightInput] = useState(() => toInputString(vitalSigns.weightKg));

  const [presentingComplaintInput, setPresentingComplaintInput] = useState(
    () => clinicalContext.presentingComplaint ?? '',
  );
  const [symptomHistoryInput, setSymptomHistoryInput] = useState(
    () => clinicalContext.symptomHistory ?? '',
  );
  const [relevantHistoryAllergiesInput, setRelevantHistoryAllergiesInput] = useState(
    () => clinicalContext.relevantHistoryAllergies ?? '',
  );
  const [clinicalFindingsInput, setClinicalFindingsInput] = useState(
    () => clinicalContext.clinicalFindings ?? '',
  );
  const [clinicalConclusionInput, setClinicalConclusionInput] = useState(
    () => clinicalContext.clinicalConclusion ?? '',
  );
  const [amendmentReasonInput, setAmendmentReasonInput] = useState(
    () => clinicalContext.amendmentReason ?? '',
  );
  const [isConfirmedInput, setIsConfirmedInput] = useState<boolean>(
    () => clinicalContext.isConfirmed || true,
  );
  const [validationError, setValidationError] = useState<string | null>(null);

  const syncDraftFromClinicalContext = () => {
    setBpSystolicInput(toInputString(vitalSigns.bpSystolicMmhg));
    setBpDiastolicInput(toInputString(vitalSigns.bpDiastolicMmhg));
    setPulseInput(toInputString(vitalSigns.pulseBpm));
    setTemperatureInput(toInputString(vitalSigns.temperatureC));
    setSpo2Input(toInputString(vitalSigns.spo2Percent));
    setHeightInput(toInputString(vitalSigns.heightCm));
    setWeightInput(toInputString(vitalSigns.weightKg));
    setPresentingComplaintInput(clinicalContext.presentingComplaint ?? '');
    setSymptomHistoryInput(clinicalContext.symptomHistory ?? '');
    setRelevantHistoryAllergiesInput(clinicalContext.relevantHistoryAllergies ?? '');
    setClinicalFindingsInput(clinicalContext.clinicalFindings ?? '');
    setClinicalConclusionInput(clinicalContext.clinicalConclusion ?? '');
    setAmendmentReasonInput('');
    setIsConfirmedInput(true);
    setValidationError(null);
  };

  if (contextSyncKey !== lastSyncedKey) {
    setLastSyncedKey(contextSyncKey);
    syncDraftFromClinicalContext();
  }

  const recordStatusMeta = RECORD_STATUS_META[clinicalContext.medicalRecordStatus] ?? {
    label: clinicalContext.medicalRecordStatus,
    tone: 'slate' as const,
  };

  const bpDisplay =
    vitalSigns.bloodPressureLabel ??
    (vitalSigns.bpSystolicMmhg !== null && vitalSigns.bpDiastolicMmhg !== null
      ? `${vitalSigns.bpSystolicMmhg}/${vitalSigns.bpDiastolicMmhg} mmHg`
      : 'Chưa đo');

  const heightWeightDisplay =
    vitalSigns.heightCm !== null || vitalSigns.weightKg !== null
      ? `${vitalSigns.heightCm ?? '—'} cm / ${vitalSigns.weightKg ?? '—'} kg`
      : 'Chưa đo';

  const parsedSysPreview = parseOptionalNumber(bpSystolicInput).value;
  const parsedDiaPreview = parseOptionalNumber(bpDiastolicInput).value;
  const parsedHeightPreview = parseOptionalNumber(heightInput).value;
  const parsedWeightPreview = parseOptionalNumber(weightInput).value;

  const liveBpLabel = formatBloodPressureLabel(parsedSysPreview, parsedDiaPreview);
  const liveBmi = calculateClinicalBmi(parsedHeightPreview, parsedWeightPreview);

  const handleFormSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setValidationError(null);
    onClearSubmitError?.();

    if (!isEditable) {
      setValidationError(
        !hasWritePermission
          ? 'Tài khoản chưa được cấp quyền ghi nhận hồ sơ bệnh án (MEDICAL_RECORD_WRITE).'
          : (readOnlyReason ?? 'Ca khám hiện ở chế độ chỉ xem, không thể lưu khám lâm sàng.'),
      );
      return;
    }

    const sys = parseOptionalNumber(bpSystolicInput);
    const dia = parseOptionalNumber(bpDiastolicInput);
    const pulse = parseOptionalNumber(pulseInput);
    const temp = parseOptionalNumber(temperatureInput);
    const spo2 = parseOptionalNumber(spo2Input);
    const height = parseOptionalNumber(heightInput);
    const weight = parseOptionalNumber(weightInput);

    if (
      sys.invalid ||
      dia.invalid ||
      pulse.invalid ||
      temp.invalid ||
      spo2.invalid ||
      height.invalid ||
      weight.invalid
    ) {
      setValidationError('Chỉ số sinh hiệu phải là giá trị số hợp lệ.');
      return;
    }

    const candidatePayload: RecordClinicalExaminationRequest = {
      presentingComplaint: presentingComplaintInput.trim() || null,
      symptomHistory: symptomHistoryInput.trim() || null,
      relevantHistoryAllergies: relevantHistoryAllergiesInput.trim() || null,
      vitalSigns: {
        bpSystolicMmhg: sys.value,
        bpDiastolicMmhg: dia.value,
        pulseBpm: pulse.value,
        temperatureC: temp.value,
        spo2Percent: spo2.value,
        heightCm: height.value,
        weightKg: weight.value,
      },
      clinicalFindings: clinicalFindingsInput.trim() || null,
      clinicalConclusion: clinicalConclusionInput.trim() || null,
      amendmentReason: amendmentReasonInput.trim() || null,
      isConfirmed: isConfirmedInput,
      expectedStateVersion: expectedStateVersion ?? null,
      expectedRevisionNo:
        clinicalContext.currentRevisionNo !== null &&
        clinicalContext.currentRevisionNo !== undefined &&
        clinicalContext.currentRevisionNo >= 1
          ? clinicalContext.currentRevisionNo
          : null,
    };

    const parsed = recordClinicalExaminationSchema.safeParse(candidatePayload);
    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0];
      setValidationError(firstIssue?.message ?? 'Dữ liệu khám lâm sàng không hợp lệ.');
      return;
    }

    await onSubmitExamination?.(candidatePayload);
  };

  return (
    <Card>
      <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <HeartPulse className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h3 className="text-sm font-bold text-navy">
              Sinh hiệu & Hồ sơ khám lâm sàng (UC31 / UC33)
            </h3>
            <p className="text-[11px] text-sub">
              {clinicalContext.medicalRecordId
                ? `Mã BA #${clinicalContext.medicalRecordId} • Phiên bản #${clinicalContext.currentRevisionNo ?? 1}`
                : 'Hồ sơ bệnh án sẽ tự động khởi tạo ở trạng thái DRAFT khi bắt đầu khám'}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={recordStatusMeta.tone}>{recordStatusMeta.label}</Badge>
          <Badge tone={isEditable ? 'emerald' : 'slate'}>
            {isEditable ? 'Cho phép ghi nhận lâm sàng' : 'Chỉ xem'}
          </Badge>
          {clinicalContext.isConfirmed && (
            <Badge tone="sky">
              Đã xác nhận phiên bản #{clinicalContext.currentRevisionNo ?? 1}
            </Badge>
          )}
          {hasWritePermission ? (
            <button
              type="button"
              disabled={!canEditClinical}
              aria-expanded={isEditable && isFormOpen}
              onClick={() => {
                setValidationError(null);
                onClearSubmitError?.();
                setFormOpen(!isFormOpen);
              }}
              className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-2.5 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary-dark disabled:cursor-not-allowed disabled:opacity-60"
            >
              <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
              <span>
                {isEditable && isFormOpen
                  ? 'Thu gọn biểu mẫu'
                  : 'Ghi nhận / Cập nhật khám lâm sàng (UC33)'}
              </span>
            </button>
          ) : (
            <span className="inline-flex items-center gap-1 rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1 text-[11px] font-semibold text-amber-800">
              <Lock className="h-3 w-3 text-amber-600" aria-hidden="true" />
              <span>Thiếu quyền MEDICAL_RECORD_WRITE</span>
            </span>
          )}
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        {!isEditable && (readOnlyReason || !hasWritePermission) && (
          <div className="rounded-xl border border-border bg-canvas px-3.5 py-2.5 text-xs text-sub flex items-center gap-2">
            <Lock className="h-3.5 w-3.5 shrink-0 text-amber-600" aria-hidden="true" />
            <span>
              {!hasWritePermission
                ? 'Tài khoản hiện tại không có quyền MEDICAL_RECORD_WRITE nên chỉ được phép xem hồ sơ khám lâm sàng.'
                : readOnlyReason}
            </span>
          </div>
        )}

        <div>
          <div className="text-xs font-bold text-navy mb-2.5 flex items-center gap-1.5">
            <Activity className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Chỉ số sinh hiệu đầu vào</span>
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2.5">
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Huyết áp</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">{bpDisplay}</div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Mạch</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.pulseBpm !== null ? `${vitalSigns.pulseBpm} bpm` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub flex items-center gap-1">
                <Thermometer className="h-3 w-3 text-amber-600" aria-hidden="true" />
                <span>Nhiệt độ</span>
              </div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.temperatureC !== null ? `${vitalSigns.temperatureC} °C` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">SpO2</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.spo2Percent !== null ? `${vitalSigns.spo2Percent}%` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub flex items-center gap-1">
                <Weight className="h-3 w-3 text-primary" aria-hidden="true" />
                <span>Cao / Nặng</span>
              </div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {heightWeightDisplay}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Chỉ số BMI</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.bmi !== null ? `${vitalSigns.bmi} kg/m²` : 'Chưa tính'}
              </div>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy flex items-center gap-1.5">
              <ClipboardList className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Lý do đến khám / Triệu chứng chính</span>
            </div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.presentingComplaint ?? 'Chưa ghi nhận triệu chứng chính.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy flex items-center gap-1.5">
              <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Bệnh sử & Diễn tiến triệu chứng</span>
            </div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.symptomHistory ?? 'Chưa ghi nhận bệnh sử chi tiết.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy">Khám lâm sàng tổng quát & Chuyên khoa</div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.clinicalFindings ?? 'Chưa có ghi nhận khám thực thể.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy">Nhận định & Hướng xử trí ban đầu</div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.clinicalConclusion ?? 'Chưa có kết luận lâm sàng.'}
            </p>
          </div>
        </div>

        {(clinicalContext.relevantHistoryAllergies || clinicalContext.amendmentReason) && (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
            {clinicalContext.relevantHistoryAllergies && (
              <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1">
                <div className="font-bold text-navy flex items-center gap-1.5">
                  <ShieldAlert className="h-3.5 w-3.5 text-rose-600" aria-hidden="true" />
                  <span>Tiền sử bệnh & Dị ứng liên quan</span>
                </div>
                <p className="text-ink leading-relaxed">
                  {clinicalContext.relevantHistoryAllergies}
                </p>
              </div>
            )}
            {clinicalContext.amendmentReason && (
              <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1">
                <div className="font-bold text-navy">Lý do cập nhật phiên bản gần nhất</div>
                <p className="text-ink leading-relaxed">{clinicalContext.amendmentReason}</p>
              </div>
            )}
          </div>
        )}

        {isEditable && isFormOpen && (
          <form
            onSubmit={(e) => void handleFormSubmit(e)}
            aria-label="Biểu mẫu ghi nhận sinh hiệu và khám lâm sàng"
            className="rounded-2xl border border-primary/30 bg-canvas/80 p-4 sm:p-5 space-y-4"
          >
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border pb-3">
              <div>
                <h4 className="text-xs font-bold text-navy flex items-center gap-1.5">
                  <Edit3 className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                  <span>Biểu mẫu ghi nhận sinh hiệu & Khám lâm sàng (UC33)</span>
                </h4>
                <p className="text-[11px] text-sub mt-0.5">
                  Mỗi lần lưu sẽ tạo một phiên bản bệnh án mới (append-only revision) và ghi nhật ký
                  kiểm toán y khoa.
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2 font-mono text-[11px]">
                <span className="rounded-md border border-border bg-white px-2 py-0.5 font-semibold text-navy">
                  Revision #{clinicalContext.currentRevisionNo ?? 1} → #
                  {(clinicalContext.currentRevisionNo ?? 1) + 1}
                </span>
                {expectedStateVersion !== null && (
                  <span className="rounded-md border border-border bg-white px-2 py-0.5 font-semibold text-sub">
                    State v{expectedStateVersion}
                  </span>
                )}
              </div>
            </div>

            {(validationError || submitError) && (
              <div role="alert">
                <Alert tone="error">{validationError ?? submitError}</Alert>
              </div>
            )}

            <div className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-xs font-bold text-navy">
                  1. Chỉ số sinh hiệu (Vital Signs)
                </span>
                <div className="flex flex-wrap items-center gap-2 text-[11px]">
                  <span className="rounded-lg border border-border bg-white px-2.5 py-1 text-sub">
                    HA gộp:{' '}
                    <strong className="font-mono text-navy">
                      {liveBpLabel ?? 'Chưa đủ cặp HA'}
                    </strong>
                  </span>
                  <span className="rounded-lg border border-border bg-white px-2.5 py-1 text-sub">
                    BMI tự động:{' '}
                    <strong className="font-mono text-navy">
                      {liveBmi !== null ? `${liveBmi} kg/m²` : 'Chưa đủ Cao/Nặng'}
                    </strong>
                  </span>
                </div>
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-3">
                <div className="space-y-1">
                  <label
                    htmlFor="uc33-bp-systolic"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    HA tâm thu (mmHg)
                  </label>
                  <input
                    id="uc33-bp-systolic"
                    aria-label="Huyết áp tâm thu (mmHg)"
                    type="number"
                    value={bpSystolicInput}
                    onChange={(e) => {
                      setBpSystolicInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="40–300"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-bp-diastolic"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    HA tâm trương (mmHg)
                  </label>
                  <input
                    id="uc33-bp-diastolic"
                    aria-label="Huyết áp tâm trương (mmHg)"
                    type="number"
                    value={bpDiastolicInput}
                    onChange={(e) => {
                      setBpDiastolicInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="20–200"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label htmlFor="uc33-pulse" className="block text-[11px] font-semibold text-navy">
                    Mạch (lần/phút)
                  </label>
                  <input
                    id="uc33-pulse"
                    aria-label="Mạch (lần/phút)"
                    type="number"
                    value={pulseInput}
                    onChange={(e) => {
                      setPulseInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="20–300"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-temperature"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Nhiệt độ (°C)
                  </label>
                  <input
                    id="uc33-temperature"
                    aria-label="Nhiệt độ (°C)"
                    type="number"
                    step="0.1"
                    value={temperatureInput}
                    onChange={(e) => {
                      setTemperatureInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="30.0–45.0"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label htmlFor="uc33-spo2" className="block text-[11px] font-semibold text-navy">
                    SpO2 (%)
                  </label>
                  <input
                    id="uc33-spo2"
                    aria-label="SpO2 (%)"
                    type="number"
                    value={spo2Input}
                    onChange={(e) => {
                      setSpo2Input(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="50–100"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-height"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Chiều cao (cm)
                  </label>
                  <input
                    id="uc33-height"
                    aria-label="Chiều cao (cm)"
                    type="number"
                    step="0.1"
                    value={heightInput}
                    onChange={(e) => {
                      setHeightInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="30–250"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-weight"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Cân nặng (kg)
                  </label>
                  <input
                    id="uc33-weight"
                    aria-label="Cân nặng (kg)"
                    type="number"
                    step="0.1"
                    value={weightInput}
                    onChange={(e) => {
                      setWeightInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="1–500"
                    className="w-full rounded-lg border border-border bg-white px-2.5 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>
              </div>
            </div>

            <div className="space-y-3">
              <div className="text-xs font-bold text-navy">
                2. Triệu chứng, Bệnh sử & Khám lâm sàng thực thể
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label
                    htmlFor="uc33-presenting-complaint"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do đến khám / Triệu chứng chính (≤ 1000 ký tự)
                  </label>
                  <textarea
                    id="uc33-presenting-complaint"
                    aria-label="Lý do đến khám / Triệu chứng chính"
                    rows={2}
                    value={presentingComplaintInput}
                    onChange={(e) => {
                      setPresentingComplaintInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="Nhập triệu chứng chính khiến bệnh nhân đến khám..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-relevant-allergies"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Tiền sử & Dị ứng liên quan (≤ 2000 ký tự • Đồng bộ cảnh báo dị ứng)
                  </label>
                  <textarea
                    id="uc33-relevant-allergies"
                    aria-label="Tiền sử & Dị ứng liên quan"
                    rows={2}
                    value={relevantHistoryAllergiesInput}
                    onChange={(e) => {
                      setRelevantHistoryAllergiesInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="Dị ứng thuốc/thức ăn, bệnh nền mạn tính..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-symptom-history"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Bệnh sử & Diễn tiến triệu chứng (≤ 4000 ký tự)
                  </label>
                  <textarea
                    id="uc33-symptom-history"
                    aria-label="Bệnh sử & Diễn tiến triệu chứng"
                    rows={3}
                    value={symptomHistoryInput}
                    onChange={(e) => {
                      setSymptomHistoryInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="Thời điểm khởi phát, tính chất triệu chứng, thuốc đã dùng..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-clinical-findings"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Khám lâm sàng tổng quát & Chuyên khoa (≤ 4000 ký tự)
                  </label>
                  <textarea
                    id="uc33-clinical-findings"
                    aria-label="Khám lâm sàng tổng quát & Chuyên khoa"
                    rows={3}
                    value={clinicalFindingsInput}
                    onChange={(e) => {
                      setClinicalFindingsInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="Khám toàn thân, tim mạch, hô hấp, tiêu hóa, thần kinh..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-clinical-conclusion"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Nhận định & Kết luận lâm sàng ban đầu (≤ 2000 ký tự)
                  </label>
                  <textarea
                    id="uc33-clinical-conclusion"
                    aria-label="Nhận định & Kết luận lâm sàng ban đầu"
                    rows={2}
                    value={clinicalConclusionInput}
                    onChange={(e) => {
                      setClinicalConclusionInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="Định hướng chẩn đoán sơ bộ và hướng xử trí tiếp theo..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="uc33-amendment-reason"
                    className="block text-[11px] font-semibold text-navy"
                  >
                    Lý do cập nhật / Ghi chú phiên bản (≤ 500 ký tự)
                  </label>
                  <textarea
                    id="uc33-amendment-reason"
                    aria-label="Lý do cập nhật / Ghi chú phiên bản"
                    rows={2}
                    value={amendmentReasonInput}
                    onChange={(e) => {
                      setAmendmentReasonInput(e.target.value);
                      if (validationError) setValidationError(null);
                    }}
                    placeholder="VD: Bổ sung sinh hiệu đo lại sau nghỉ ngơi 10 phút..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>
              </div>
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pt-2 border-t border-border">
              <label
                htmlFor="uc33-is-confirmed"
                className="inline-flex items-center gap-2 text-xs font-medium text-navy cursor-pointer"
              >
                <input
                  id="uc33-is-confirmed"
                  aria-label="Xác nhận thông tin khám lâm sàng ở phiên bản này"
                  type="checkbox"
                  checked={isConfirmedInput}
                  onChange={(e) => setIsConfirmedInput(e.target.checked)}
                  className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                />
                <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                <span>Xác nhận thông tin khám lâm sàng ở phiên bản này</span>
              </label>

              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={() => {
                    syncDraftFromClinicalContext();
                    onClearSubmitError?.();
                  }}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas disabled:opacity-60"
                >
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Khôi phục dữ liệu hiện tại</span>
                </button>
                <Button
                  type="submit"
                  disabled={isSubmitting}
                  className="inline-flex items-center gap-1.5"
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{isSubmitting ? 'Đang lưu bệnh án...' : 'Lưu khám lâm sàng (UC33)'}</span>
                </Button>
              </div>
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
