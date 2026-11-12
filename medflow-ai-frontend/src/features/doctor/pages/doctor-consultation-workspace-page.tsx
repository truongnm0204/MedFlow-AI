import { ArrowLeft, RefreshCw, Search } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useRoleAccess } from '@/features/auth/hooks/use-role-access';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card, CardContent } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { ConsultationAllowedActionsCard } from '../components/consultation-allowed-actions-card';
import { ConsultationCompleteVisitPanel } from '../components/consultation-complete-visit-panel';
import { ConsultationDiagnosisAndOrdersCard } from '../components/consultation-diagnosis-and-orders-card';
import { ConsultationPatientHeaderCard } from '../components/consultation-patient-header-card';
import { ConsultationQueueContextCard } from '../components/consultation-queue-context-card';
import { ConsultationVitalsAndClinicalCard } from '../components/consultation-vitals-and-clinical-card';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { CONSULTATION_QUICK_SWITCH_ITEMS } from '../data/consultation-workspace-mock';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  useCompleteVisit,
  useConsultationWorkspace,
  useCreateDiagnosticOrder,
  useDiagnosticOrderCatalog,
  useOpenConsultationWorkspace,
  useRecordClinicalExamination,
} from '../hooks/use-consultation-workspace';
import { consultationIdentifierSchema } from '../schemas/consultation-workspace.schema';
import type {
  CompleteVisitRequest,
  ConsultationWorkspaceActionDto,
  CreateDiagnosticOrderRequest,
  RecordClinicalExaminationRequest,
} from '../types/consultation-workspace.types';

const DEFAULT_VISIT_CODE = 'VIS-2026-0212';

export interface DoctorConsultationWorkspacePageProps {
  explicitPermissions?: readonly string[] | null;
}

export function DoctorConsultationWorkspacePage({
  explicitPermissions,
}: DoctorConsultationWorkspacePageProps = {}) {
  const navigate = useNavigate();
  const location = useLocation();
  const { visitIdOrCode: routeVisitIdOrCode } = useParams<{ visitIdOrCode?: string }>();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { hasPermission } = useRoleAccess(explicitPermissions);

  const canWriteMedicalRecord =
    user === null && explicitPermissions === undefined
      ? true
      : hasPermission('MEDICAL_RECORD_WRITE');

  const canOrderDiagnostics =
    user === null && explicitPermissions === undefined ? true : hasPermission('DIAGNOSTIC_ORDER');

  const activeIdentifier = routeVisitIdOrCode?.trim() || DEFAULT_VISIT_CODE;
  const isClinicalRoute = location.pathname.endsWith('/clinical-examination');
  const isDiagnosticOrderRoute =
    location.pathname.endsWith('/diagnostic-orders') ||
    location.pathname.endsWith('/diagnostic-order') ||
    location.pathname.endsWith('/additional-diagnostic-orders') ||
    location.pathname.endsWith('/additional-diagnostic-order');
  const isCompleteVisitRoute =
    location.pathname.endsWith('/complete') || location.pathname.endsWith('/complete-visit');
  const [lookupInput, setLookupInput] = useState(activeIdentifier);
  const [lookupValidationError, setLookupValidationError] = useState<string | null>(null);
  const [openError, setOpenError] = useState<string | null>(null);
  const [recordError, setRecordError] = useState<string | null>(null);
  const [diagnosticOrderError, setDiagnosticOrderError] = useState<string | null>(null);
  const [completeVisitError, setCompleteVisitError] = useState<string | null>(null);
  const [isClinicalFormOpen, setIsClinicalFormOpen] = useState<boolean>(isClinicalRoute);
  const [isDiagnosticOrderFormOpen, setIsDiagnosticOrderFormOpen] =
    useState<boolean>(isDiagnosticOrderRoute);
  const [isCompleteVisitFormOpen, setIsCompleteVisitFormOpen] =
    useState<boolean>(isCompleteVisitRoute);
  const [selectedActionNotice, setSelectedActionNotice] = useState<string | null>(null);

  const resetTransientState = (nextIdentifier: string) => {
    setLookupInput(nextIdentifier);
    setLookupValidationError(null);
    setOpenError(null);
    setRecordError(null);
    setDiagnosticOrderError(null);
    setCompleteVisitError(null);
    setSelectedActionNotice(null);
  };

  const { data, isPending, isError, error, refetch } = useConsultationWorkspace(activeIdentifier);
  const catalogQuery = useDiagnosticOrderCatalog(
    activeIdentifier,
    Boolean(data) && isDiagnosticOrderFormOpen,
  );
  const openMutation = useOpenConsultationWorkspace();
  const recordMutation = useRecordClinicalExamination();
  const createOrderMutation = useCreateDiagnosticOrder();
  const completeVisitMutation = useCompleteVisit();
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
    setRecordError(null);
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

  const handleRecordClinicalExamination = async (payload: RecordClinicalExaminationRequest) => {
    setRecordError(null);
    setOpenError(null);
    setSelectedActionNotice(null);

    try {
      await recordMutation.mutateAsync({
        idOrCode: activeIdentifier,
        payload,
      });
    } catch (err) {
      setRecordError(
        getErrorMessage(err, 'Không thể lưu thông tin khám lâm sàng cho lượt khám này.'),
      );
    }
  };

  const handleCreateDiagnosticOrder = async (
    payload: CreateDiagnosticOrderRequest,
  ): Promise<boolean> => {
    setDiagnosticOrderError(null);
    setOpenError(null);
    setRecordError(null);
    setSelectedActionNotice(null);

    try {
      await createOrderMutation.mutateAsync({
        idOrCode: activeIdentifier,
        payload,
      });
      setIsDiagnosticOrderFormOpen(false);
      return true;
    } catch (err) {
      setDiagnosticOrderError(
        getErrorMessage(err, 'Không thể tạo phiếu chỉ định cận lâm sàng cho lượt khám này.'),
      );
      return false;
    }
  };

  const handleCompleteVisit = async (payload: CompleteVisitRequest): Promise<boolean> => {
    setCompleteVisitError(null);
    setOpenError(null);
    setRecordError(null);
    setDiagnosticOrderError(null);
    setSelectedActionNotice(null);

    try {
      await completeVisitMutation.mutateAsync({
        idOrCode: activeIdentifier,
        payload,
      });
      setIsCompleteVisitFormOpen(false);
      return true;
    } catch (err) {
      setCompleteVisitError(
        getErrorMessage(err, 'Không thể hoàn tất lượt khám và khóa hồ sơ bệnh án cho ca khám này.'),
      );
      return false;
    }
  };

  const handleTriggerAction = (action: ConsultationWorkspaceActionDto) => {
    if (!action.enabled) return;

    if (action.id === 'open-consultation') {
      void handleOpenSession();
      return;
    }

    if (action.id === 'view-medical-history' || action.ucCode === 'UC32') {
      const targetVisit = data?.visit.visitCode ?? activeIdentifier;
      navigate(`/doctor/visits/${encodeURIComponent(targetVisit)}/medical-history`);
      return;
    }

    if (action.id === 'record-clinical-examination' || action.ucCode === 'UC33') {
      if (!canWriteMedicalRecord || !data?.canEditClinical) {
        return;
      }
      setRecordError(null);
      setIsClinicalFormOpen(true);
      setSelectedActionNotice(
        `Đang mở biểu mẫu "${action.label}" (${action.ucCode}) cho bệnh nhân ${data.patient.fullName} (${data.visit.visitCode}).`,
      );
      return;
    }

    if (
      action.id === 'create-diagnostic-order' ||
      action.id === 'create-additional-diagnostic-order' ||
      action.ucCode === 'UC34'
    ) {
      if (!canOrderDiagnostics || !data) {
        return;
      }
      setDiagnosticOrderError(null);
      setIsDiagnosticOrderFormOpen(true);
      setSelectedActionNotice(
        `Đang mở biểu mẫu "${action.label}" (${action.ucCode}) cho bệnh nhân ${data.patient.fullName} (${data.visit.visitCode}).`,
      );
      return;
    }

    if (action.id === 'complete-visit' || action.ucCode === 'UC38') {
      if (!canWriteMedicalRecord || !data?.canEditClinical) {
        return;
      }
      setCompleteVisitError(null);
      setIsCompleteVisitFormOpen(true);
      setSelectedActionNotice(
        `Đang mở biểu mẫu "${action.label}" (${action.ucCode}) cho bệnh nhân ${data.patient.fullName} (${data.visit.visitCode}).`,
      );
      return;
    }

    if (action.id === 'record-diagnosis' || action.ucCode === 'UC36') {
      if (!canWriteMedicalRecord || !data?.canEditClinical) return;
      navigate(`/doctor/consultation/${encodeURIComponent(data.visit.visitCode)}/diagnosis`);
      return;
    }

    if (action.id === 'review-diagnostic-results' || action.ucCode === 'UC35') {
      const targetVisit = data?.visit.visitCode ?? activeIdentifier;
      navigate(`/doctor/consultation/${encodeURIComponent(targetVisit)}/diagnostic-results`);
      return;
    }

    if (action.id === 'create-prescription' || action.ucCode === 'UC37') {
      if (!action.enabled) return;
      const targetVisit = data?.visit.visitCode ?? activeIdentifier;
      navigate(`/doctor/consultation/${encodeURIComponent(targetVisit)}/prescription`);
      return;
    }

    setSelectedActionNotice(
      `Đã chọn thao tác "${action.label}" (${action.ucCode}) cho bệnh nhân ${data?.patient.fullName ?? ''} (${data?.visit.visitCode ?? activeIdentifier}).`,
    );
  };

  const openAction = data?.allowedActions.find((a) => a.id === 'open-consultation');
  const diagnosticOrderAction = data?.allowedActions.find(
    (a) =>
      a.id === 'create-diagnostic-order' ||
      a.id === 'create-additional-diagnostic-order' ||
      a.ucCode === 'UC34',
  );
  const completeVisitAction = data?.allowedActions.find(
    (a) => a.id === 'complete-visit' || a.ucCode === 'UC38',
  );
  const canCreateDiagnosticOrder =
    Boolean(diagnosticOrderAction?.enabled) ||
    (data?.workspaceState === 'IN_PROGRESS' &&
      data.clinicalContext.medicalRecordStatus !== 'FINALIZED');
  const canCompleteVisit =
    Boolean(completeVisitAction?.enabled) ||
    (Boolean(data?.canEditClinical) &&
      data?.workspaceState === 'IN_PROGRESS' &&
      data.clinicalContext.medicalRecordStatus !== 'FINALIZED' &&
      data.clinicalContext.diagnosticSummary.allMandatoryResultsReady);
  const completeVisitDisabledReason =
    completeVisitAction?.disabledReason ?? data?.readOnlyReason ?? null;
  const diagnosticDisabledReason =
    diagnosticOrderAction?.disabledReason ??
    catalogQuery.data?.disabledReason ??
    data?.readOnlyReason ??
    null;
  const effectiveDiagnosticOrders =
    data?.clinicalContext.diagnosticOrders ?? catalogQuery.data?.existingOrders ?? [];
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
                      hasWritePermission={canWriteMedicalRecord}
                      readOnlyReason={data.readOnlyReason}
                      expectedStateVersion={data.queue?.stateVersion ?? null}
                      isSubmitting={recordMutation.isPending}
                      submitError={recordError}
                      isFormOpen={isClinicalFormOpen}
                      onToggleForm={setIsClinicalFormOpen}
                      onSubmitExamination={handleRecordClinicalExamination}
                      onClearSubmitError={() => setRecordError(null)}
                    />
                    <ConsultationDiagnosisAndOrdersCard
                      diagnoses={data.clinicalContext.diagnoses}
                      diagnosticSummary={data.clinicalContext.diagnosticSummary}
                      prescriptionSummary={data.clinicalContext.prescriptionSummary}
                      diagnosticOrders={effectiveDiagnosticOrders}
                      catalogServices={catalogQuery.data?.services ?? []}
                      isCatalogLoading={catalogQuery.isPending}
                      catalogError={
                        catalogQuery.isError
                          ? getErrorMessage(
                              catalogQuery.error,
                              'Không thể tải danh mục dịch vụ cận lâm sàng.',
                            )
                          : null
                      }
                      onRetryCatalog={() => void catalogQuery.refetch()}
                      canCreateDiagnosticOrder={canCreateDiagnosticOrder}
                      hasDiagnosticOrderPermission={canOrderDiagnostics}
                      diagnosticDisabledReason={diagnosticDisabledReason}
                      expectedStateVersion={data.queue?.stateVersion ?? null}
                      isCreatingDiagnosticOrder={createOrderMutation.isPending}
                      diagnosticOrderSubmitError={diagnosticOrderError}
                      isDiagnosticOrderFormOpen={isDiagnosticOrderFormOpen}
                      onToggleDiagnosticOrderForm={setIsDiagnosticOrderFormOpen}
                      onSubmitDiagnosticOrder={handleCreateDiagnosticOrder}
                      onClearDiagnosticOrderSubmitError={() => setDiagnosticOrderError(null)}
                    />
                    <ConsultationCompleteVisitPanel
                      workspaceState={data.workspaceState}
                      visit={data.visit}
                      clinicalContext={data.clinicalContext}
                      canCompleteVisit={canCompleteVisit}
                      hasWritePermission={canWriteMedicalRecord}
                      disabledReason={completeVisitDisabledReason}
                      expectedStateVersion={data.queue?.stateVersion ?? null}
                      expectedRevisionNo={data.clinicalContext.currentRevisionNo ?? null}
                      isSubmitting={completeVisitMutation.isPending}
                      submitError={completeVisitError}
                      isFormOpen={isCompleteVisitFormOpen}
                      onToggleForm={setIsCompleteVisitFormOpen}
                      onSubmitCompleteVisit={handleCompleteVisit}
                      onClearSubmitError={() => setCompleteVisitError(null)}
                    />
                  </div>

                  <div className="xl:col-span-4 space-y-6">
                    <ConsultationQueueContextCard queue={data.queue} />
                    <ConsultationAllowedActionsCard
                      actions={data.allowedActions}
                      selectedActionNotice={selectedActionNotice}
                      isOpening={openMutation.isPending}
                      isRecordingClinical={recordMutation.isPending}
                      isCreatingDiagnosticOrder={createOrderMutation.isPending}
                      isCompletingVisit={completeVisitMutation.isPending}
                      hasWritePermission={canWriteMedicalRecord}
                      hasDiagnosticOrderPermission={canOrderDiagnostics}
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
