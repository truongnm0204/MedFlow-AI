import { ArrowLeft, ArrowRight, CheckCircle2, Plus, Printer, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';

import { DiagnosticResultDetailPanel } from '../components/diagnostic-result-detail-panel';
import { DiagnosticResultListCard } from '../components/diagnostic-result-list-card';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  REVIEW_DIAGNOSTIC_ACTIVE_STEP,
  REVIEW_DIAGNOSTIC_MOCK,
  REVIEW_DIAGNOSTIC_STEPS,
} from '../data/review-diagnostic-mock';
import type { DiagnosticResultItem } from '../types/review-diagnostic.types';

export function DoctorReviewDiagnosticsPage() {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const { patient, results } = REVIEW_DIAGNOSTIC_MOCK;
  const visitCode = params.visitIdOrCode?.trim() || patient.visitCode;
  const [selectedId, setSelectedId] = useState<string>(results[0].id);
  const [notice, setNotice] = useState<string | null>(null);

  const selected = results.find((r) => r.id === selectedId) ?? results[0];
  const doctorName = user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;
  const consultationPath = params.visitIdOrCode
    ? `/doctor/consultation/${params.visitIdOrCode}`
    : '/doctor/consultation';

  const handleDownloadPdf = (result: DiagnosticResultItem) =>
    setNotice(`Đang tải file PDF kết quả gốc: ${result.pdfFileName}`);
  const handleSharePortal = (result: DiagnosticResultItem) =>
    setNotice(`Đã chia sẻ phiếu ${result.serviceCode} lên portal bệnh nhân.`);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="diagnostics"
        doctorName={doctorName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/doctor');
          else if (item === 'assigned-queue') navigate('/doctor/assigned-queue');
          else if (item === 'consultation') navigate('/doctor/consultation');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate(consultationPath)}
              aria-label="Quay lại phiên khám"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy">Clinical Workbench</h1>
              <p className="truncate text-[11px] text-sub">
                Xem lại kết quả cận lâm sàng trả về (SCR-DIA-02)
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

        <main className="flex-1 space-y-4 overflow-y-auto p-6 pb-24">
          {/* Patient banner */}
          <section
            aria-label="Thông tin bệnh nhân"
            className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-white px-5 py-3"
          >
            <div>
              <p className="text-base font-bold text-navy">{patient.fullName}</p>
              <p className="text-xs text-sub">
                <span className="font-mono">{patient.patientCode}</span> •{' '}
                <span className="font-mono">{visitCode}</span>
              </p>
            </div>
            <div
              role="note"
              className="inline-flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3 py-1.5 text-xs font-semibold text-rose-700"
            >
              <ShieldAlert className="h-4 w-4" aria-hidden="true" />
              <span>Dị ứng: {patient.allergies.join(', ')}</span>
            </div>
          </section>

          {/* Stepper */}
          <ol aria-label="Các bước khám bệnh" className="flex flex-wrap gap-2">
            {REVIEW_DIAGNOSTIC_STEPS.map((label, index) => {
              const step = index + 1;
              const isActive = step === REVIEW_DIAGNOSTIC_ACTIVE_STEP;
              const isDone = step < REVIEW_DIAGNOSTIC_ACTIVE_STEP;
              return (
                <li
                  key={label}
                  aria-current={isActive ? 'step' : undefined}
                  className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-semibold ${
                    isActive
                      ? 'border-primary bg-primary text-white'
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

          {/* Readiness banner */}
          <div
            role="status"
            className="flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-xs font-bold text-emerald-800"
          >
            <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span>
              TẤT CẢ KẾT QUẢ ĐÃ SẴN SÀNG ({results.length}/{results.length} DỊCH VỤ HOÀN TẤT) • ĐỦ
              ĐIỀU KIỆN CHẨN ĐOÁN XÁC ĐỊNH
            </span>
          </div>

          {notice ? (
            <div role="status" aria-live="polite">
              <Alert tone="info">{notice}</Alert>
            </div>
          ) : null}

          <div className="grid gap-6 lg:grid-cols-2">
            <section aria-label="Danh mục kết quả cận lâm sàng" className="space-y-3">
              <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                DANH MỤC KẾT QUẢ CẬN LÂM SÀNG ({results.length} Dịch vụ)
              </h2>
              {results.map((result) => (
                <DiagnosticResultListCard
                  key={result.id}
                  result={result}
                  isSelected={result.id === selected.id}
                  onSelect={setSelectedId}
                />
              ))}
            </section>

            <section aria-label="Phiếu kết quả CLS" className="space-y-3">
              <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                PHIẾU KẾT QUẢ CLS - SCR-DIA-09
              </h2>
              <DiagnosticResultDetailPanel
                result={selected}
                onDownloadPdf={handleDownloadPdf}
                onSharePortal={handleSharePortal}
              />
            </section>
          </div>
        </main>

        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 print:hidden">
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => navigate(consultationPath)}
              aria-label="Mở chỉ định thêm dịch vụ CLS"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span>Mở chỉ định thêm dịch vụ CLS</span>
            </button>
            <button
              type="button"
              onClick={() => window.print()}
              aria-label="In toàn bộ phiếu kết quả (Ctrl+P)"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
            >
              <Printer className="h-4 w-4" aria-hidden="true" />
              <span>In toàn bộ phiếu kết quả (Ctrl+P)</span>
            </button>
          </div>
          <button
            type="button"
            onClick={() => navigate(consultationPath)}
            aria-label="Tiếp tục chẩn đoán và kết luận (SCR-CON-04)"
            className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
          >
            <span>TIẾP TỤC CHẨN ĐOÁN & KẾT LUẬN (SCR-CON-04)</span>
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </footer>
      </div>
    </div>
  );
}
