import { ArrowLeft, Pill, Plus, Trash2 } from 'lucide-react';
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

import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import { useConsultationWorkspace, useSavePrescription } from '../hooks/use-consultation-workspace';
import {
  blankPrescriptionLine,
  createPrescriptionFormSchema,
  toCreatePrescriptionRequest,
  type PrescriptionLineFormValues,
} from '../schemas/create-prescription.schema';
import type { PrescriptionFulfillmentChoice } from '../types/consultation-workspace.types';

const FULFILLMENT_OPTIONS: { value: PrescriptionFulfillmentChoice; label: string }[] = [
  { value: 'HOSPITAL', label: 'Cấp thuốc tại bệnh viện' },
  { value: 'HOSPITAL_PHARMACY', label: 'Nhà thuốc bệnh viện' },
  { value: 'EXTERNAL_PHARMACY', label: 'Nhà thuốc ngoài' },
];

export function DoctorCreatePrescriptionPage({
  explicitPermissions,
}: { explicitPermissions?: readonly string[] | null } = {}) {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { hasPermission } = useRoleAccess(explicitPermissions);
  const canIssuePrescription =
    user === null && explicitPermissions === undefined ? true : hasPermission('PRESCRIPTION_ISSUE');

  const visitIdOrCode = params.visitIdOrCode?.trim() ?? '';
  const workspace = useConsultationWorkspace(visitIdOrCode, visitIdOrCode.length > 0);
  const save = useSavePrescription();
  const { data, isLoading, isError, error, refetch } = workspace;

  const [syncedVisitId, setSyncedVisitId] = useState<number | null>(null);
  const [fulfillmentChoice, setFulfillmentChoice] =
    useState<PrescriptionFulfillmentChoice>('HOSPITAL');
  const [items, setItems] = useState<PrescriptionLineFormValues[]>([blankPrescriptionLine()]);
  const [notice, setNotice] = useState<{
    tone: 'error' | 'info' | 'success';
    message: string;
  } | null>(null);

  if (data && data.visit.visitId !== syncedVisitId) {
    setSyncedVisitId(data.visit.visitId);
    setItems([blankPrescriptionLine()]);
    setFulfillmentChoice('HOSPITAL');
    setNotice(null);
  }

  const doctorName = user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;
  const prescriptionAction = data?.allowedActions.find(
    (action) => action.id === 'create-prescription' || action.ucCode === 'UC37',
  );
  const canSave = Boolean(data) && canIssuePrescription && Boolean(prescriptionAction?.enabled);
  const readOnlyReason = !data
    ? null
    : !canIssuePrescription
      ? 'Tài khoản không có quyền kê đơn thuốc (PRESCRIPTION_ISSUE).'
      : !prescriptionAction?.enabled
        ? (prescriptionAction?.disabledReason ??
          data.readOnlyReason ??
          'Lượt khám hiện không cho phép kê đơn thuốc.')
        : null;

  const primaryDiagnosis =
    data?.clinicalContext.diagnoses.find((item) => item.isPrimary) ??
    data?.clinicalContext.diagnoses[0] ??
    null;
  const summary = data?.clinicalContext.prescriptionSummary;
  const allergyText =
    data?.patient.allergiesSummary?.trim() ||
    data?.clinicalContext.relevantHistoryAllergies?.trim() ||
    null;
  const backTarget = visitIdOrCode
    ? `/doctor/consultation/${encodeURIComponent(data?.visit.visitCode ?? visitIdOrCode)}`
    : '/doctor/consultation';

  const updateLine = (index: number, patch: Partial<PrescriptionLineFormValues>) => {
    setItems((prev) =>
      prev.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)),
    );
  };

  const handleSave = async () => {
    const parsed = createPrescriptionFormSchema.safeParse({ fulfillmentChoice, items });
    if (!parsed.success) {
      setNotice({
        tone: 'error',
        message: parsed.error.issues[0]?.message ?? 'Thông tin đơn thuốc chưa hợp lệ.',
      });
      return;
    }

    try {
      const saved = await save.mutateAsync({
        idOrCode: visitIdOrCode,
        payload: toCreatePrescriptionRequest(parsed.data, {
          expectedStateVersion: data?.queue?.stateVersion ?? null,
          expectedRevisionNo: data?.clinicalContext.currentRevisionNo ?? null,
        }),
      });
      const savedSummary = saved.clinicalContext.prescriptionSummary;
      setNotice({
        tone: 'success',
        message: `Đã lưu đơn nháp ${savedSummary.latestPrescriptionCode ?? ''} (${savedSummary.itemCount} khoản thuốc). Đơn được phát hành khi hoàn tất lượt khám (UC38).`,
      });
    } catch (err) {
      setNotice({
        tone: 'error',
        message: getErrorMessage(err, 'Không thể lưu đơn thuốc. Vui lòng thử lại.'),
      });
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="prescriptions"
        doctorName={doctorName}
        roomLabel={roomLabel}
        onItemSelect={(id) => {
          if (id === 'dashboard') navigate('/doctor');
          else if (id === 'consultation') navigate('/doctor/consultation');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate(backTarget)}
              aria-label="Quay lại bàn khám lâm sàng"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="flex items-center gap-2 truncate text-sm font-bold text-navy">
                <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Kê đơn thuốc ngoại trú (SCR-RX-01)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                {data ? (
                  <>
                    Lượt khám <span className="font-mono font-bold">{data.visit.visitCode}</span>
                    {' • '}
                    Mã đơn{' '}
                    <span className="font-mono font-bold text-primary">
                      {summary?.latestPrescriptionCode ?? 'Chưa lưu'}
                    </span>
                  </>
                ) : visitIdOrCode ? (
                  <>
                    Lượt khám <span className="font-mono font-bold">{visitIdOrCode}</span>
                  </>
                ) : (
                  'Mở từ bàn khám lâm sàng (UC37)'
                )}
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
          {!visitIdOrCode ? (
            <Card>
              <CardHeader>
                <h2 className="text-sm font-bold text-navy">Chưa chọn lượt khám để kê đơn</h2>
              </CardHeader>
              <CardContent className="space-y-3">
                <p className="text-xs text-sub">
                  Mở đơn thuốc từ bàn khám lâm sàng bằng thao tác &quot;Kê đơn thuốc ngoại trú&quot;
                  (UC37).
                </p>
                <Button
                  type="button"
                  variant="primary"
                  size="sm"
                  onClick={() => navigate('/doctor/consultation')}
                >
                  Đến bàn khám lâm sàng
                </Button>
              </CardContent>
            </Card>
          ) : isLoading ? (
            <Card>
              <CardContent>
                <div role="status" className="flex items-center gap-3 py-4 text-sm text-sub">
                  <Spinner />
                  <span>Đang tải lượt khám để kê đơn…</span>
                </div>
              </CardContent>
            </Card>
          ) : isError ? (
            <div className="space-y-3" data-testid="create-prescription-error">
              <Alert tone="error">
                {getErrorMessage(error, 'Không thể tải lượt khám để kê đơn.')}
              </Alert>
              <Button type="button" variant="secondary" size="sm" onClick={() => void refetch()}>
                Thử lại
              </Button>
            </div>
          ) : data ? (
            <>
              <section
                aria-label="Thông tin bệnh nhân và đơn hiện có"
                className="space-y-3 rounded-2xl border border-border bg-white p-5 shadow-2xs"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-base font-bold text-navy">{data.patient.fullName}</p>
                  <span className="text-xs text-sub">
                    {data.patient.patientCode} • {data.patient.genderLabel}
                    {data.patient.age !== null ? ` • ${data.patient.age} tuổi` : ''}
                  </span>
                  <Badge tone={summary && summary.itemCount > 0 ? 'sky' : 'slate'}>
                    {summary?.latestPrescriptionStatus ?? 'Chưa có đơn'}
                  </Badge>
                </div>
                <p className="text-xs text-sub">
                  Chẩn đoán chính:{' '}
                  <strong className="text-navy">
                    {primaryDiagnosis
                      ? `${primaryDiagnosis.code} — ${primaryDiagnosis.name}`
                      : 'Chưa ghi nhận'}
                  </strong>
                </p>
                {allergyText ? <Alert tone="warning">Cảnh báo dị ứng: {allergyText}</Alert> : null}
                {summary && summary.itemCount > 0 ? (
                  <Alert tone="info">
                    Đơn {summary.latestPrescriptionCode ?? ''} đang{' '}
                    {summary.latestPrescriptionStatus} với {summary.itemCount} khoản. API không trả
                    từng dòng thuốc. Lưu lại sẽ thay toàn bộ dòng của đơn nháp DRAFT/PENDING.
                  </Alert>
                ) : (
                  <p className="text-xs text-sub">
                    Kê đơn tạo hoặc thay đơn nháp DRAFT. Phát hành ISSUED khi hoàn tất lượt khám,
                    không phải trên màn này.
                  </p>
                )}
              </section>

              {readOnlyReason ? <Alert tone="info">{readOnlyReason}</Alert> : null}
              {notice ? <Alert tone={notice.tone}>{notice.message}</Alert> : null}

              <section aria-label="Dòng thuốc" className="space-y-3">
                <div className="flex items-end justify-between gap-3">
                  <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                    Nơi cấp thuốc
                    <select
                      value={fulfillmentChoice}
                      disabled={!canSave || save.isPending}
                      onChange={(event) =>
                        setFulfillmentChoice(event.target.value as PrescriptionFulfillmentChoice)
                      }
                      className="rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink disabled:opacity-60"
                    >
                      {FULFILLMENT_OPTIONS.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    disabled={!canSave || save.isPending || items.length >= 20}
                    onClick={() => setItems((prev) => [...prev, blankPrescriptionLine()])}
                  >
                    <Plus className="h-4 w-4" aria-hidden="true" />
                    Thêm dòng thuốc
                  </Button>
                </div>

                {items.map((line, index) => (
                  <Card key={`line-${index}`}>
                    <CardContent className="grid gap-3 md:grid-cols-2">
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        {`Mã thuốc dòng ${index + 1}`}
                        <input
                          value={line.medicineCode}
                          disabled={!canSave || save.isPending}
                          onChange={(event) =>
                            updateLine(index, { medicineCode: event.target.value })
                          }
                          className="rounded-lg border border-border px-3 py-2 text-sm font-normal text-ink disabled:opacity-60"
                        />
                      </label>
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        {`ID thuốc dòng ${index + 1}`}
                        <input
                          inputMode="numeric"
                          value={line.medicineId}
                          disabled={!canSave || save.isPending}
                          onChange={(event) =>
                            updateLine(index, { medicineId: event.target.value })
                          }
                          className="rounded-lg border border-border px-3 py-2 text-sm font-normal text-ink disabled:opacity-60"
                        />
                      </label>
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        {`Số lượng dòng ${index + 1}`}
                        <input
                          inputMode="decimal"
                          value={line.prescribedQuantity}
                          disabled={!canSave || save.isPending}
                          onChange={(event) =>
                            updateLine(index, { prescribedQuantity: event.target.value })
                          }
                          className="rounded-lg border border-border px-3 py-2 text-sm font-normal text-ink disabled:opacity-60"
                        />
                      </label>
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        {`Lịch dùng dòng ${index + 1}`}
                        <input
                          value={line.doseScheduleText}
                          disabled={!canSave || save.isPending}
                          onChange={(event) =>
                            updateLine(index, { doseScheduleText: event.target.value })
                          }
                          className="rounded-lg border border-border px-3 py-2 text-sm font-normal text-ink disabled:opacity-60"
                        />
                      </label>
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy md:col-span-2">
                        {`Hướng dẫn dòng ${index + 1}`}
                        <input
                          value={line.instructions}
                          disabled={!canSave || save.isPending}
                          onChange={(event) =>
                            updateLine(index, { instructions: event.target.value })
                          }
                          className="rounded-lg border border-border px-3 py-2 text-sm font-normal text-ink disabled:opacity-60"
                        />
                      </label>
                      <div className="md:col-span-2">
                        <Button
                          type="button"
                          variant="secondary"
                          size="sm"
                          disabled={!canSave || save.isPending || items.length === 1}
                          onClick={() =>
                            setItems((prev) => prev.filter((_, lineIndex) => lineIndex !== index))
                          }
                        >
                          <Trash2 className="h-4 w-4" aria-hidden="true" />
                          {`Xóa dòng ${index + 1}`}
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                ))}

                {canSave ? (
                  <Button
                    type="button"
                    variant="primary"
                    disabled={save.isPending}
                    onClick={() => void handleSave()}
                  >
                    Lưu đơn nháp
                  </Button>
                ) : null}
              </section>
            </>
          ) : null}
        </main>
      </div>
    </div>
  );
}
