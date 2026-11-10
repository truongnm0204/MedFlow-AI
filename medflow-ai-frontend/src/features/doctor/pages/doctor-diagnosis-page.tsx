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
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosisConclusionPanel } from '../components/diagnosis-conclusion-panel';
import { DoctorSidebar } from '../components/doctor-sidebar';
import {
  DIAGNOSIS_CONCLUSION_CONTEXT_MOCK,
  ICD10_CATALOG_MOCK,
} from '../data/diagnosis-conclusion-mock';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import { validateDiagnosisConclusion } from '../schemas/diagnosis-conclusion.schema';
import type {
  DiagnosisConclusionFieldErrors,
  DiagnosisConclusionFormValues,
} from '../types/diagnosis-conclusion.types';

const CONSULTATION_STEPS = [
  'Tiền sử & Khảo sát',
  'Khám lâm sàng & Sinh hiệu',
  'Chỉ định Cận lâm sàng',
  'Kết quả CLS trả về',
  'Chẩn đoán ICD-10 & Kết luận (SCR-CON-04)',
] as const;

export function DoctorDiagnosisPage() {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const { patient, evidence, initialValues } = DIAGNOSIS_CONCLUSION_CONTEXT_MOCK;
  const visitCode = params.visitIdOrCode?.trim() || patient.visitCode;

  const [values, setValues] = useState<DiagnosisConclusionFormValues>(initialValues);
  const [errors, setErrors] = useState<DiagnosisConclusionFieldErrors>({});
  const [statusNotice, setStatusNotice] = useState<{
    tone: 'info' | 'success';
    message: string;
  } | null>(null);
  const [isFinalized, setIsFinalized] = useState(false);

  const doctorName = user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const handlePatch = (patch: Partial<DiagnosisConclusionFormValues>) => {
    setValues((prev) => ({ ...prev, ...patch }));
    setErrors((prev) => {
      const next = { ...prev };
      for (const key of Object.keys(patch)) {
        delete next[key as keyof DiagnosisConclusionFieldErrors];
      }
      if (patch.followUp) {
        delete next.followUpDate;
        delete next.followUpTime;
        delete next.followUpRoom;
      }
      return next;
    });
  };

  const handleSaveDraft = () => {
    setStatusNotice({
      tone: 'info',
      message: `Đã lưu bản nháp chẩn đoán cho lượt khám ${visitCode}.`,
    });
  };

  const handleSubmit = () => {
    const validationErrors = validateDiagnosisConclusion(values);
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      setStatusNotice(null);
      return;
    }

    setErrors({});
    setIsFinalized(true);
    setStatusNotice({
      tone: 'success',
      message: `Đã phát hành chẩn đoán chính thức (${values.primaryIcd10Code}) và hoàn tất ca khám ${visitCode}.`,
    });
  };

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
              onClick={() => navigate('/doctor/consultation')}
              aria-label="Quay lại phiên khám"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy">
                Chẩn đoán Bệnh ICD-10 &amp; Hoàn tất Ca khám (SCR-CON-04)
              </h1>
              <p className="truncate text-[11px] text-sub">
                Clinical Workbench • Lượt khám <span className="font-mono">{visitCode}</span>
              </p>
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

        <main className="flex-1 space-y-5 overflow-y-auto p-6 pb-28">
          {/* Patient Header Banner */}
          <section
            aria-label="Thông tin bệnh nhân"
            className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-border bg-white px-5 py-4 shadow-2xs"
          >
            <div className="space-y-1">
              <div className="flex flex-wrap items-center gap-2.5">
                <p className="text-base font-bold text-navy">{patient.fullName}</p>
                <span className="text-xs text-sub">
                  • {patient.gender}, {patient.ageLabel}
                </span>
                <Badge tone={isFinalized ? 'emerald' : 'sky'}>
                  {isFinalized
                    ? 'Đã phát hành kết luận (Completed)'
                    : 'Đang kết luận chẩn đoán (Diagnosis Finalization)'}
                </Badge>
              </div>
              <p className="font-mono text-xs text-sub">
                Mã BN: <strong className="text-navy">{patient.patientCode}</strong> • Mã lượt khám:{' '}
                <strong className="text-navy">{visitCode}</strong> • Bệnh nền:{' '}
                <span className="font-sans font-medium text-ink">{patient.chronicDisease}</span>
              </p>
            </div>

            <div
              role="note"
              className="inline-flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700"
            >
              <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span>Dị ứng thuốc: {patient.allergies}</span>
            </div>
          </section>

          {/* 5-step Consultation Stepper */}
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

          {statusNotice && (
            <div role="status" aria-live="polite">
              <Alert tone={statusNotice.tone}>{statusNotice.message}</Alert>
            </div>
          )}

          {/* Section 1: Clinical Evidence & Diagnostic Results */}
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
                <p className="font-bold uppercase tracking-wide text-sub">
                  Triệu chứng lâm sàng ghi nhận tại buồng
                </p>
                <p className="mt-1 leading-relaxed text-ink">{evidence.symptomsSummary}</p>
              </div>

              <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                {evidence.tests.map((test) => (
                  <div
                    key={test.id}
                    className="rounded-lg border border-border bg-white p-3 text-xs space-y-1"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-bold text-navy">{test.title}</span>
                      <Badge tone="emerald">Hoàn tất</Badge>
                    </div>
                    <p className="text-ink leading-relaxed">{test.result}</p>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>

          {/* Section 2 & 3 & 4: ICD-10 Diagnosis + Clinical Conclusion + Follow-up */}
          <DiagnosisConclusionPanel
            values={values}
            errors={errors}
            catalog={ICD10_CATALOG_MOCK}
            allergies={patient.allergies}
            onChange={handlePatch}
          />
        </main>

        {/* Sticky Footer Action Bar */}
        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm">
          <div className="flex flex-wrap items-center gap-2.5">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={handleSaveDraft}
              className="gap-1.5"
            >
              <Save className="h-4 w-4" aria-hidden="true" />
              <span>Lưu nháp</span>
            </Button>

            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() =>
                setStatusNotice({
                  tone: 'info',
                  message: 'Chuyển tiếp sang kê đơn thuốc điện tử ngoại trú (SCR-RX-01).',
                })
              }
              className="gap-1.5"
            >
              <Pill className="h-4 w-4" aria-hidden="true" />
              <span>Kê đơn thuốc ngoại trú (SCR-RX-01)</span>
            </Button>
          </div>

          <Button
            type="button"
            variant="primary"
            size="sm"
            onClick={handleSubmit}
            className="gap-1.5 shadow-xs"
          >
            {isFinalized ? (
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            ) : (
              <ClipboardCheck className="h-4 w-4" aria-hidden="true" />
            )}
            <span>Hoàn tất ca khám &amp; Phát hành Kết luận</span>
          </Button>
        </footer>
      </div>
    </div>
  );
}
