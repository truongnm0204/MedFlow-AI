import { ArrowLeft, RefreshCw, Search } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card, CardContent } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { ConsultationAllowedActionsCard } from '../components/consultation-allowed-actions-card';
import { ConsultationDiagnosisAndOrdersCard } from '../components/consultation-diagnosis-and-orders-card';
import { ConsultationPatientHeaderCard } from '../components/consultation-patient-header-card';
import { ConsultationQueueContextCard } from '../components/consultation-queue-context-card';
import { ConsultationVitalsAndClinicalCard } from '../components/consultation-vitals-and-clinical-card';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { CONSULTATION_QUICK_SWITCH_ITEMS } from '../data/consultation-workspace-mock';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  useConsultationWorkspace,
  useOpenConsultationWorkspace,
} from '../hooks/use-consultation-workspace';
import { consultationIdentifierSchema } from '../schemas/consultation-workspace.schema';
import type { ConsultationWorkspaceActionDto } from '../types/consultation-workspace.types';

const DEFAULT_VISIT_CODE = 'VIS-2026-0212';

export function DoctorConsultationWorkspacePage() {
  const navigate = useNavigate();
  const { visitIdOrCode: routeVisitIdOrCode } = useParams<{ visitIdOrCode?: string }>();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeIdentifier = routeVisitIdOrCode?.trim() || DEFAULT_VISIT_CODE;
  const [lookupInput, setLookupInput] = useState(activeIdentifier);
  const [lookupValidationError, setLookupValidationError] = useState<string | null>(null);
  const [openError, setOpenError] = useState<string | null>(null);
  const [selectedActionNotice, setSelectedActionNotice] = useState<string | null>(null);

  const resetTransientState = (nextIdentifier: string) => {
    setLookupInput(nextIdentifier);
    setLookupValidationError(null);
    setOpenError(null);
    setSelectedActionNotice(null);
  };

  const { data, isPending, isError, error, refetch } = useConsultationWorkspace(activeIdentifier);
  const openMutation = useOpenConsultationWorkspace();

  const doctorDisplayName =
    data?.visit.assignedDoctorName?.trim() ||
    user?.fullName?.trim() ||
    DOCTOR_WORKBENCH_META.defaultDoctorName;

  const roomLabel = data?.visit.roomCode
    ? `${data.visit.roomCode} • ${data.visit.departmentName}`
    : `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const facilityName = data?.visit.hospitalName || DOCTOR_WORKBENCH_META.facilityName;

  const handleLookupSubmit = (e: FormEvent) => {
    e.preventDefault();
    setLookupValidationError(null);

    const parsed = consultationIdentifierSchema.safeParse(lookupInput);
    if (!parsed.success) {
      setLookupValidationError(parsed.error.issues[0]?.message ?? 'Mã lượt khám không hợp lệ.');
      return;
    }

    resetTransientState(parsed.data);
    navigate(`/doctor/consultation/${encodeURIComponent(parsed.data)}`);
  };

  const handleOpenSession = async (notes?: string) => {
    setOpenError(null);
    setSelectedActionNotice(null);

    try {
      await openMutation.mutateAsync({
        idOrCode: activeIdentifier,
        payload: {
          expectedStateVersion: data?.queue?.stateVersion ?? null,
          notes: notes ?? null,
        },
      });
    } catch (err) {
      setOpenError(getErrorMessage(err, 'Không thể mở phiên khám lâm sàng cho lượt khám này.'));
    }
  };

  const handleTriggerAction = (action: ConsultationWorkspaceActionDto) => {
    if (!action.enabled) return;

    if (action.id === 'open-consultation') {
      void handleOpenSession();
      return;
    }

    setSelectedActionNotice(
      `Đã chọn thao tác "${action.label}" (${action.ucCode}) cho bệnh nhân ${data?.patient.fullName ?? ''} (${data?.visit.visitCode ?? activeIdentifier}).`,
    );
  };

  const openAction = data?.allowedActions.find((a) => a.id === 'open-consultation');

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="consultation"
        doctorName={doctorDisplayName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') {
            navigate('/doctor');
            return;
          }
          if (item === 'assigned-queue') {
            navigate('/doctor/assigned-queue');
          }
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-border px-6 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3 min-w-0">
            <button
              type="button"
              onClick={() => navigate('/doctor/assigned-queue')}
              aria-label="Quay lại hàng đợi được phân công"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="text-sm font-bold text-navy truncate">
                Không gian khám lâm sàng (UC31)
              </h1>
              <div className="text-[11px] text-sub truncate">
                {facilityName} • {roomLabel} • Mã ca:{' '}
                <span className="font-mono font-semibold text-navy">{activeIdentifier}</span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => void refetch()}
              disabled={isPending}
              aria-label="Làm mới không gian khám"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas disabled:opacity-60"
            >
              <RefreshCw
                aria-hidden="true"
                className={`h-3.5 w-3.5 ${isPending ? 'animate-spin' : ''}`}
              />
              <span className="hidden sm:inline">Làm mới</span>
            </button>
            <div className="flex items-center gap-2.5 pl-3 border-l border-border">
              <div className="w-9 h-9 rounded-full bg-primary/10 border border-primary/20 text-primary flex items-center justify-center font-bold text-xs">
                {doctorDisplayName.slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-navy">{doctorDisplayName}</div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-sub hover:text-rose-600 transition-colors"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          <Card>
            <CardContent className="py-3.5 flex flex-col lg:flex-row lg:items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="text-xs font-bold text-navy mr-1">Ca khám trong ca trực:</span>
                {CONSULTATION_QUICK_SWITCH_ITEMS.map((item) => {
                  const isCurrent =
                    activeIdentifier.toUpperCase() === item.visitCode ||
                    data?.visit.visitCode === item.visitCode;
                  return (
                    <button
                      key={item.visitCode}
                      type="button"
                      aria-pressed={isCurrent}
                      onClick={() => {
                        resetTransientState(item.visitCode);
                        navigate(`/doctor/consultation/${item.visitCode}`);
                      }}
                      className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-colors ${
                        isCurrent
                          ? 'bg-primary text-white'
                          : 'bg-canvas text-sub hover:bg-border/60'
                      }`}
                    >
                      <span className="font-mono font-bold">{item.ticketNo}</span> •{' '}
                      {item.patientName}
                    </button>
                  );
                })}
              </div>

              <form onSubmit={handleLookupSubmit} className="flex items-center gap-2">
                <div className="relative">
                  <Search
                    className="h-3.5 w-3.5 text-sub absolute left-2.5 top-1/2 -translate-y-1/2"
                    aria-hidden="true"
                  />
                  <input
                    type="search"
                    value={lookupInput}
                    onChange={(e) => {
                      setLookupInput(e.target.value);
                      if (lookupValidationError) setLookupValidationError(null);
                    }}
                    placeholder="Mã lượt khám (VD: VIS-2026-0213)..."
                    aria-label="Tra cứu mã lượt khám"
                    className="w-full sm:w-56 rounded-lg border border-border bg-white pl-8 pr-3 py-1.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>
                <button
                  type="submit"
                  className="rounded-lg bg-navy px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-navy/90"
                >
                  Mở ca khám
                </button>
              </form>
            </CardContent>
          </Card>

          {lookupValidationError && (
            <div role="alert">
              <Alert tone="error">{lookupValidationError}</Alert>
            </div>
          )}

          {openError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>{openError}</span>
                  <button
                    type="button"
                    onClick={() => setOpenError(null)}
                    className="rounded-lg bg-rose-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-rose-700"
                  >
                    Đóng
                  </button>
                </div>
              </Alert>
            </div>
          )}

          {isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>
                    {getErrorMessage(error, 'Không thể tải dữ liệu không gian khám lâm sàng')}
                  </span>
                  <button
                    type="button"
                    onClick={() => void refetch()}
                    className="rounded-lg bg-rose-600 px-3 py-1 text-xs font-semibold text-white transition-colors hover:bg-rose-700"
                  >
                    Thử lại
                  </button>
                </div>
              </Alert>
            </div>
          )}

          {isPending ? (
            <div
              role="status"
              className="bg-white rounded-2xl border border-border p-12 flex flex-col items-center justify-center gap-3 shadow-sm"
            >
              <Spinner className="h-6 w-6 text-primary" />
              <div className="text-xs font-medium text-sub">
                Đang tải không gian khám lâm sàng...
              </div>
            </div>
          ) : (
            data && (
              <>
                <ConsultationPatientHeaderCard
                  workspaceState={data.workspaceState}
                  actionOutcome={data.actionOutcome}
                  canEditClinical={data.canEditClinical}
                  readOnlyReason={data.readOnlyReason}
                  visit={data.visit}
                  patient={data.patient}
                  queue={data.queue}
                  openAction={openAction}
                  isOpening={openMutation.isPending}
                  onOpenSession={(notes) => void handleOpenSession(notes)}
                />

                <div className="grid grid-cols-1 xl:grid-cols-12 gap-6">
                  <div className="xl:col-span-8 space-y-6">
                    <ConsultationVitalsAndClinicalCard
                      clinicalContext={data.clinicalContext}
                      canEditClinical={data.canEditClinical}
                    />
                    <ConsultationDiagnosisAndOrdersCard
                      diagnoses={data.clinicalContext.diagnoses}
                      diagnosticSummary={data.clinicalContext.diagnosticSummary}
                      prescriptionSummary={data.clinicalContext.prescriptionSummary}
                    />
                  </div>

                  <div className="xl:col-span-4 space-y-6">
                    <ConsultationQueueContextCard queue={data.queue} />
                    <ConsultationAllowedActionsCard
                      actions={data.allowedActions}
                      selectedActionNotice={selectedActionNotice}
                      isOpening={openMutation.isPending}
                      onTriggerAction={handleTriggerAction}
                    />
                  </div>
                </div>
              </>
            )
          )}
        </main>
      </div>
    </div>
  );
}
