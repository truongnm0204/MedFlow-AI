import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Clock,
  Plus,
  Printer,
  ShieldAlert,
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useRoleAccess } from '@/features/auth/hooks/use-role-access';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card, CardContent } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { DiagnosticResultDetailPanel } from '../components/diagnostic-result-detail-panel';
import { DiagnosticResultListCard } from '../components/diagnostic-result-list-card';
import { DiagnosticResultsReviewForm } from '../components/diagnostic-results-review-form';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import {
  REVIEW_DIAGNOSTIC_ACTIVE_STEP,
  REVIEW_DIAGNOSTIC_STEPS,
} from '../data/review-diagnostic-mock';
import {
  useDiagnosticResultsReview,
  useReviewDiagnosticResults,
} from '../hooks/use-consultation-workspace';
import type { ReviewDiagnosticResultsInput } from '../schemas/review-diagnostic-results.schema';
import type { DiagnosticResultsReviewResponseDto } from '../types/review-diagnostic.types';

export interface DoctorReviewDiagnosticsPageProps {
  explicitPermissions?: readonly string[] | null;
}

function ReadinessBanner({ data }: { data: DiagnosticResultsReviewResponseDto }) {
  const ready = data.readyResultsCount;
  const total = data.totalOrdersCount;
  if (total === 0) return null;

  if (ready === total) {
    return (
      <div
        role="status"
        data-testid="review-diagnostic-readiness"
        className="flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-xs font-bold text-emerald-800"
      >
        <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span>
          TẤT CẢ KẾT QUẢ ĐÃ SẴN SÀNG ({ready}/{total} DỊCH VỤ HOÀN TẤT) • ĐỦ ĐIỀU KIỆN CHẨN ĐOÁN XÁC
          ĐỊNH
        </span>
      </div>
    );
  }

  if (data.mandatoryPendingCount === 0) {
    return (
      <div
        role="status"
        data-testid="review-diagnostic-readiness"
        className="flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-xs font-bold text-emerald-800"
      >
        <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span>
          ĐÃ CÓ {ready}/{total} KẾT QUẢ • KẾT QUẢ BẮT BUỘC ĐÃ ĐỦ, CÓ THỂ CHẨN ĐOÁN XÁC ĐỊNH
        </span>
      </div>
    );
  }

  return (
    <div
      role="status"
      data-testid="review-diagnostic-readiness"
      className="flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-5 py-3 text-xs font-bold text-amber-800"
    >
      <Clock className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span>
        ĐANG CHỜ {data.pendingResultsCount} KẾT QUẢ ({data.mandatoryPendingCount} BẮT BUỘC) • CHƯA
        ĐỦ ĐIỀU KIỆN CHẨN ĐOÁN XÁC ĐỊNH
      </span>
    </div>
  );
}

export function DoctorReviewDiagnosticsPage({
  explicitPermissions,
}: DoctorReviewDiagnosticsPageProps = {}) {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { hasPermission } = useRoleAccess(explicitPermissions);

  const canWriteMedicalRecord =
    user === null && explicitPermissions === undefined
      ? true
      : hasPermission('MEDICAL_RECORD_WRITE');

  const visitIdOrCode = params.visitIdOrCode?.trim() ?? '';
  const { data, isPending, isError, error, refetch } = useDiagnosticResultsReview(visitIdOrCode);
  const reviewMutation = useReviewDiagnosticResults();

  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [reviewSuccess, setReviewSuccess] = useState<string | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);

  const doctorName = user?.fullName?.trim() || DOCTOR_WORKBENCH_META.defaultDoctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;
  const consultationPath = visitIdOrCode
    ? `/doctor/consultation/${encodeURIComponent(visitIdOrCode)}`
    : '/doctor/consultation';

  const visibleItems = (data?.items ?? []).filter((item) => item.orderStatus !== 'CANCELLED');
  const selected =
    visibleItems.find((item) => item.orderId === selectedOrderId) ??
    visibleItems.find((item) => item.isResultReady) ??
    visibleItems[0];
  const readyOrderIds = visibleItems
    .filter((item) => item.isResultReady)
    .map((item) => item.orderId);

  const handleReview = async (input: ReviewDiagnosticResultsInput): Promise<boolean> => {
    setReviewSuccess(null);
    setReviewError(null);
    try {
      await reviewMutation.mutateAsync({
        idOrCode: visitIdOrCode,
        payload: {
          orderIds: input.orderIds,
          clinicalInterpretation: input.clinicalInterpretation || null,
        },
      });
      setReviewSuccess(
        `Đã ghi nhận xem xét ${input.orderIds.length} kết quả cận lâm sàng cho lượt khám ${data?.visitCode ?? visitIdOrCode}.`,
      );
      return true;
    } catch (err) {
      setReviewError(getErrorMessage(err, 'Không thể ghi nhận xem xét kết quả cận lâm sàng.'));
      return false;
    }
  };

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
          else if (item === 'diagnostics') navigate('/doctor/diagnostic-results/tracking');
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
          {!visitIdOrCode ? (
            <Card data-testid="review-diagnostic-no-visit">
              <CardContent className="space-y-3">
                <h2 className="text-sm font-bold text-navy">Chưa chọn lượt khám</h2>
                <p className="text-sm text-sub">
                  Mở trang này từ phiên khám lâm sàng hoặc màn hình theo dõi kết quả CLS.
                </p>
                <button
                  type="button"
                  onClick={() => navigate('/doctor/diagnostic-results/tracking')}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
                >
                  Đến theo dõi kết quả CLS
                </button>
              </CardContent>
            </Card>
          ) : isPending ? (
            <div
              role="status"
              data-testid="review-diagnostic-loading"
              className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-border bg-white p-12 shadow-sm"
            >
              <Spinner className="h-6 w-6 text-primary" />
              <div className="text-xs font-medium text-sub">Đang tải kết quả cận lâm sàng...</div>
            </div>
          ) : isError || !data ? (
            <div data-testid="review-diagnostic-error-banner" className="space-y-3">
              <Alert tone="error">
                {getErrorMessage(error, 'Không thể tải kết quả cận lâm sàng của lượt khám này.')}
              </Alert>
              <button
                type="button"
                onClick={() => void refetch()}
                className="rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-rose-700"
              >
                Thử lại
              </button>
            </div>
          ) : (
            <>
              <section
                aria-label="Thông tin bệnh nhân"
                className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-white px-5 py-3"
              >
                <div>
                  <p className="text-base font-bold text-navy">{data.patient.fullName}</p>
                  <p className="text-xs text-sub">
                    <span className="font-mono">{data.patient.patientCode}</span> •{' '}
                    <span className="font-mono">{data.visitCode}</span>
                  </p>
                </div>
                {data.patient.allergiesSummary ? (
                  <div
                    role="note"
                    className="inline-flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3 py-1.5 text-xs font-semibold text-rose-700"
                  >
                    <ShieldAlert className="h-4 w-4" aria-hidden="true" />
                    <span>Dị ứng: {data.patient.allergiesSummary}</span>
                  </div>
                ) : null}
              </section>

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

              <ReadinessBanner data={data} />

              {data.totalOrdersCount === 0 ? (
                <div
                  data-testid="review-diagnostic-empty"
                  className="rounded-xl border border-border bg-white px-5 py-8 text-center text-sm text-sub"
                >
                  Lượt khám chưa có phiếu chỉ định cận lâm sàng nào.
                </div>
              ) : (
                <div className="grid gap-6 lg:grid-cols-2">
                  <section aria-label="Danh mục kết quả cận lâm sàng" className="space-y-3">
                    <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                      DANH MỤC KẾT QUẢ CẬN LÂM SÀNG ({data.totalOrdersCount} Dịch vụ)
                    </h2>
                    {visibleItems.map((result, index) => (
                      <DiagnosticResultListCard
                        key={result.orderId}
                        result={result}
                        index={index + 1}
                        isSelected={result.orderId === selected?.orderId}
                        onSelect={setSelectedOrderId}
                      />
                    ))}
                  </section>

                  <section aria-label="Phiếu kết quả CLS" className="space-y-3">
                    <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                      PHIẾU KẾT QUẢ CLS - SCR-DIA-09
                    </h2>
                    {selected ? <DiagnosticResultDetailPanel result={selected} /> : null}
                  </section>
                </div>
              )}

              {reviewSuccess ? (
                <div role="status" aria-live="polite" data-testid="diagnostic-review-success">
                  <Alert tone="success">{reviewSuccess}</Alert>
                </div>
              ) : null}
              {reviewError ? (
                <div data-testid="diagnostic-review-submit-error">
                  <Alert tone="error">{reviewError}</Alert>
                </div>
              ) : null}

              {canWriteMedicalRecord && data.canAcknowledgeDiagnosticResults ? (
                <DiagnosticResultsReviewForm
                  readyOrderIds={readyOrderIds}
                  isSubmitting={reviewMutation.isPending}
                  onSubmit={handleReview}
                />
              ) : canWriteMedicalRecord && data.disabledReason ? (
                <div data-testid="diagnostic-review-disabled-reason">
                  <Alert tone="info">{data.disabledReason}</Alert>
                </div>
              ) : null}
            </>
          )}
        </main>

        {visitIdOrCode ? (
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
              onClick={() => navigate(`${consultationPath}/diagnosis`)}
              aria-label="Tiếp tục chẩn đoán và kết luận (SCR-CON-04)"
              className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
            >
              <span>TIẾP TỤC CHẨN ĐOÁN & KẾT LUẬN (SCR-CON-04)</span>
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </button>
          </footer>
        ) : null}
      </div>
    </div>
  );
}
