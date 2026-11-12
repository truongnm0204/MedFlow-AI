import axios from 'axios';
import {
  ArrowLeft,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  Pill,
  Save,
  ShieldAlert,
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useRoleAccess } from '@/features/auth/hooks/use-role-access';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { DiagnosisConclusionPanel } from '../components/diagnosis-conclusion-panel';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  useConsultationWorkspace,
  useRecordDiagnosis,
  type RecordDiagnosisVariables,
} from '../hooks/use-consultation-workspace';
import { validateDiagnosisConclusion } from '../schemas/diagnosis-conclusion.schema';
import type {
  DiagnosisConclusionFieldErrors,
  DiagnosisConclusionFormValues,
  DiagnosisSaveMode,
  Icd10Option,
} from '../types/diagnosis-conclusion.types';
import type { ConsultationWorkspaceResponseDto } from '../types/consultation-workspace.types';
import {
  toDiagnosisFormValues,
  toDiagnosisOptionMap,
  toRecordDiagnosisRequest,
} from '../utils/diagnosis-conclusion-mapper';

const CONSULTATION_STEPS = [
  'Tiền sử & Khảo sát',
  'Khám lâm sàng & Sinh hiệu',
  'Chỉ định Cận lâm sàng',
  'Kết quả CLS trả về',
  'Chẩn đoán ICD-10 & Kết luận (SCR-CON-04)',
] as const;

const PAGE_TITLE = 'Chẩn đoán Bệnh ICD-10 & Kết luận lâm sàng (SCR-CON-04)';

const READ_ONLY_FALLBACK = 'Không thể ghi nhận chẩn đoán ở trạng thái hiện tại của lượt khám.';
const WRITE_PERMISSION_REASON =
  'Tài khoản chưa được cấp quyền ghi nhận hồ sơ bệnh án (MEDICAL_RECORD_WRITE).';

interface SaveNotice {
  tone: 'info' | 'success' | 'error';
  message: string;
  conflict: boolean;
}

interface DiagnosisConclusionContentProps {
  workspace: ConsultationWorkspaceResponseDto;
  isSaving: boolean;
  onReload: () => void;
  onSave: (variables: RecordDiagnosisVariables) => Promise<ConsultationWorkspaceResponseDto>;
}

function DiagnosisConclusionContent({
  workspace,
  isSaving,
  onReload,
  onSave,
}: DiagnosisConclusionContentProps) {
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const { hasPermission } = useRoleAccess();
  const [values, setValues] = useState<DiagnosisConclusionFormValues>(() =>
    toDiagnosisFormValues(workspace),
  );
  const [optionsByCode, setOptionsByCode] = useState<Record<string, Icd10Option>>(() =>
    toDiagnosisOptionMap(workspace),
  );
  const [errors, setErrors] = useState<DiagnosisConclusionFieldErrors>({});
  const [notice, setNotice] = useState<SaveNotice | null>(null);

  const canWrite = user === null ? true : hasPermission('MEDICAL_RECORD_WRITE');
  const recordAction = workspace.allowedActions.find(
    (action) => action.id === 'record-diagnosis' || action.ucCode === 'UC36',
  );
  const isEditable = canWrite && workspace.canEditClinical && recordAction?.enabled === true;
  const readOnlyReason = !canWrite
    ? WRITE_PERMISSION_REASON
    : (recordAction?.disabledReason ?? workspace.readOnlyReason ?? READ_ONLY_FALLBACK);

  const diagnoses = workspace.clinicalContext.diagnoses ?? [];
  const primaryDiagnosis = diagnoses.find((item) => item.isPrimary);
  const primaryConfirmed = primaryDiagnosis?.status === 'CONFIRMED';
  const statusBadge = primaryConfirmed
    ? { tone: 'emerald' as const, label: 'Đã xác nhận chẩn đoán' }
    : diagnoses.length > 0
      ? { tone: 'amber' as const, label: 'Chẩn đoán sơ bộ (nháp)' }
      : { tone: 'sky' as const, label: 'Đang kết luận chẩn đoán' };

  const symptomSummary =
    [workspace.clinicalContext.presentingComplaint, workspace.clinicalContext.clinicalFindings]
      .filter(Boolean)
      .join(' • ') || 'Chưa ghi nhận triệu chứng lâm sàng.';
  const diagnosticTests = (workspace.clinicalContext.diagnosticOrders ?? []).filter(
    (order) => order.status !== 'CANCELLED',
  );
  const allergies = workspace.patient.allergiesSummary;
  const visitCode = workspace.visit.visitCode;
  const ageLabel = workspace.patient.age != null ? `${workspace.patient.age} tuổi` : null;
  const demographicLabel = [workspace.patient.genderLabel, ageLabel].filter(Boolean).join(', ');

  const handleOptionResolved = (option: Icd10Option) => {
    setOptionsByCode((prev) => ({
      ...prev,
      [option.code]: { code: option.code, nameVi: option.nameVi },
    }));
  };

  const handlePatch = (patch: Partial<DiagnosisConclusionFormValues>) => {
    setValues((prev) => ({ ...prev, ...patch }));
    setErrors((prev) => {
      const next = { ...prev };
      for (const key of Object.keys(patch)) {
        delete next[key as keyof DiagnosisConclusionFieldErrors];
      }
      return next;
    });
  };

  const handleSave = async (mode: DiagnosisSaveMode) => {
    const validationErrors = validateDiagnosisConclusion(values, mode);
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      setNotice(null);
      return;
    }

    setErrors({});
    try {
      await onSave({
        idOrCode: visitCode,
        payload: toRecordDiagnosisRequest(values, workspace, mode),
      });
      const primaryCode = values.primaryIcd10Code.trim().toUpperCase();
      setNotice(
        mode === 'draft'
          ? {
              tone: 'info',
              conflict: false,
              message: `Đã lưu nháp chẩn đoán cho lượt khám ${visitCode}.`,
            }
          : {
              tone: 'success',
              conflict: false,
              message: `Đã xác nhận chẩn đoán chính ${primaryCode} và kết luận lâm sàng cho lượt khám ${visitCode}.`,
            },
      );
    } catch (error) {
      setNotice({
        tone: 'error',
        conflict: axios.isAxiosError(error) && error.response?.status === 409,
        message: getErrorMessage(error, 'Không thể lưu chẩn đoán. Vui lòng thử lại.'),
      });
    }
  };

  return (
    <>
      <main className="flex-1 space-y-5 overflow-y-auto p-6">
        <section
          aria-label="Thông tin bệnh nhân"
          className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-border bg-white px-5 py-4 shadow-2xs"
        >
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <p className="text-base font-bold text-navy">{workspace.patient.fullName}</p>
              {demographicLabel && <span className="text-xs text-sub">• {demographicLabel}</span>}
              <Badge tone={statusBadge.tone}>{statusBadge.label}</Badge>
            </div>
            <p className="font-mono text-xs text-sub">
              Mã BN: <strong className="text-navy">{workspace.patient.patientCode}</strong> • Mã
              lượt khám: <strong className="font-mono text-navy">{visitCode}</strong>
            </p>
          </div>

          {allergies?.trim() && (
            <div
              role="note"
              className="inline-flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700"
            >
              <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span>Dị ứng thuốc: {allergies.trim()}</span>
            </div>
          )}
        </section>

        <ConsultationStepper />

        {notice && (
          <div role="status" aria-live="polite" className="space-y-2">
            <Alert tone={notice.tone}>{notice.message}</Alert>
            {notice.conflict && (
              <Button type="button" variant="secondary" size="sm" onClick={onReload}>
                Tải lại dữ liệu lượt khám
              </Button>
            )}
          </div>
        )}

        {!isEditable && <Alert tone="warning">{readOnlyReason}</Alert>}

        <Card>
          <CardHeader className="py-3.5">
            <div className="flex items-center gap-2">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              <h2 className="text-sm font-bold text-navy">
                1. BẰNG CHỨNG LÂM SÀNG &amp; KẾT QUẢ CẬN LÂM SÀNG
              </h2>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="rounded-lg border border-border bg-canvas p-3.5 text-xs">
              <p className="font-bold tracking-wide text-sub uppercase">
                Triệu chứng lâm sàng ghi nhận tại buồng
              </p>
              <p className="mt-1 leading-relaxed text-ink">{symptomSummary}</p>
            </div>

            {diagnosticTests.length === 0 ? (
              <p className="text-xs text-sub">Lượt khám chưa có chỉ định cận lâm sàng.</p>
            ) : (
              <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                {diagnosticTests.map((order) => (
                  <div
                    key={order.orderId}
                    className="space-y-1 rounded-lg border border-border bg-white p-3 text-xs"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-bold text-navy">{order.serviceName}</span>
                      <span className="flex flex-wrap items-center gap-1">
                        <Badge tone={order.isResultFinalized ? 'emerald' : 'amber'}>
                          {order.isResultFinalized ? 'Hoàn tất' : 'Chưa có kết quả'}
                        </Badge>
                        {order.requiredForConclusion && <Badge tone="slate">Bắt buộc</Badge>}
                      </span>
                    </div>
                    <p className="leading-relaxed text-ink">
                      {order.conclusion ?? order.resultText ?? 'Chưa có kết quả.'}
                    </p>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <DiagnosisConclusionPanel
          values={values}
          errors={errors}
          optionsByCode={optionsByCode}
          onOptionResolved={handleOptionResolved}
          allergies={allergies}
          readOnly={!isEditable}
          onChange={handlePatch}
        />
      </main>

      <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm">
        <div className="flex flex-wrap items-center gap-2.5">
          {isEditable && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              disabled={isSaving}
              onClick={() => void handleSave('draft')}
              className="gap-1.5"
            >
              <Save className="h-4 w-4" aria-hidden="true" />
              <span>Lưu nháp</span>
            </Button>
          )}

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() =>
              navigate(`/doctor/consultation/${encodeURIComponent(visitCode)}/prescription`)
            }
            className="gap-1.5"
          >
            <Pill className="h-4 w-4" aria-hidden="true" />
            <span>Kê đơn thuốc ngoại trú (UC37)</span>
          </Button>

          {primaryConfirmed && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() =>
                navigate(`/doctor/consultation/${encodeURIComponent(visitCode)}/complete`)
              }
              className="gap-1.5"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              <span>Hoàn tất lượt khám (UC38)</span>
            </Button>
          )}
        </div>

        {isEditable && (
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={isSaving}
            onClick={() => void handleSave('confirm')}
            className="gap-1.5 shadow-xs"
          >
            <ClipboardCheck className="h-4 w-4" aria-hidden="true" />
            <span>Xác nhận chẩn đoán &amp; kết luận</span>
          </Button>
        )}
      </footer>
    </>
  );
}

function ConsultationStepper() {
  return (
    <ol aria-label="Tiến trình phiên khám" className="flex flex-wrap gap-2">
      {CONSULTATION_STEPS.map((label, index) => {
        const step = index + 1;
        const isActive = step === 5;
        const isDone = step < 5;
        return (
          <li
            key={label}
            aria-current={isActive ? 'step' : undefined}
            className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-semibold ${
              isActive
                ? 'border-primary bg-primary text-white shadow-2xs'
                : isDone
                  ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                  : 'border-border bg-white text-sub'
            }`}
          >
            <span>{step}.</span>
            <span>{label}</span>
          </li>
        );
      })}
    </ol>
  );
}

export function DoctorDiagnosisPage() {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const logout = useLogout();
  const visitIdOrCode = params.visitIdOrCode?.trim() ?? '';
  const workspaceQuery = useConsultationWorkspace(visitIdOrCode);
  const recordDiagnosis = useRecordDiagnosis();

  const doctorName = user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;
  const workspace = workspaceQuery.data;
  const backTarget = workspace
    ? `/doctor/consultation/${encodeURIComponent(workspace.visit.visitCode)}`
    : visitIdOrCode
      ? `/doctor/consultation/${encodeURIComponent(visitIdOrCode)}`
      : '/doctor/consultation';
  const headerVisitCode = workspace?.visit.visitCode ?? visitIdOrCode;

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="consultation"
        doctorName={doctorName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/doctor');
          else if (item === 'assigned-queue') navigate('/doctor/assigned-queue');
          else if (item === 'consultation') navigate('/doctor/consultation');
          else if (item === 'medical-history') navigate('/doctor/medical-history');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate(backTarget)}
              aria-label="Quay lại phiên khám"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy">{PAGE_TITLE}</h1>
              {headerVisitCode && (
                <p className="truncate text-[11px] text-sub">
                  Clinical Workbench • Lượt khám{' '}
                  <span className="font-mono">{headerVisitCode}</span>
                </p>
              )}
            </div>
          </div>

          <button
            type="button"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
          >
            Đăng xuất
          </button>
        </header>

        {!visitIdOrCode ? (
          <main className="flex-1 space-y-5 overflow-y-auto p-6">
            <Card>
              <CardContent className="space-y-3 py-8 text-center">
                <p className="text-sm font-semibold text-navy">Chưa chọn lượt khám</p>
                <Button
                  type="button"
                  variant="primary"
                  size="sm"
                  onClick={() => navigate('/doctor/assigned-queue')}
                >
                  Mở hàng đợi được phân công
                </Button>
              </CardContent>
            </Card>
          </main>
        ) : workspaceQuery.isLoading || (!workspace && !workspaceQuery.isError) ? (
          <main className="flex-1 space-y-5 overflow-y-auto p-6">
            <div role="status" className="flex flex-col items-center justify-center gap-3 py-20">
              <Spinner className="h-6 w-6 text-primary" />
              <p className="text-sm text-sub">Đang tải không gian khám…</p>
            </div>
          </main>
        ) : !workspace ? (
          <main className="flex-1 space-y-5 overflow-y-auto p-6">
            <div className="space-y-3">
              <Alert tone="error">
                {getErrorMessage(workspaceQuery.error, 'Không tải được thông tin lượt khám.')}
              </Alert>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => void workspaceQuery.refetch()}
              >
                Thử lại
              </Button>
            </div>
          </main>
        ) : (
          <DiagnosisConclusionContent
            key={workspace.visit.visitCode}
            workspace={workspace}
            isSaving={recordDiagnosis.isPending}
            onReload={() => void workspaceQuery.refetch()}
            onSave={recordDiagnosis.mutateAsync}
          />
        )}
      </div>
    </div>
  );
}
