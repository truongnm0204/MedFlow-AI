import axios from 'axios';
import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  PackageCheck,
  Printer,
  RefreshCw,
} from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { DispenseIdentityVerificationCard } from '../components/dispense-identity-verification-card';
import {
  DispenseMedicationCounselingList,
  type DispenseLineEditState,
} from '../components/dispense-medication-counseling-list';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  useConfirmMedicineDispense,
  useDispenseConfirmContext,
} from '../hooks/use-pharmacy-workbench';
import type { PharmacistHandoverCheck } from '../types/dispense-confirm.types';

function createAttemptToken(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

function isValidationOrConflictError(error: unknown): boolean {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status;
    return status === 400 || status === 409 || status === 422;
  }
  if (error instanceof Error && 'status' in error) {
    const status = (error as { status?: number }).status;
    return status === 400 || status === 409 || status === 422;
  }
  return false;
}

export function DispenseConfirmPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeRxCode = params.prescriptionCode?.trim() || searchParams.get('rxCode')?.trim() || '';

  const contextQuery = useDispenseConfirmContext(activeRxCode);
  const confirmMutation = useConfirmMedicineDispense(activeRxCode);

  const [identityOverrides, setIdentityOverrides] = useState<Record<string, boolean>>({});
  const [checkOverridesByRx, setCheckOverridesByRx] = useState<
    Record<string, Record<string, boolean>>
  >({});
  const [lineEditsByRx, setLineEditsByRx] = useState<
    Record<string, Record<string, DispenseLineEditState>>
  >({});
  const [dispensedCodeByRx, setDispensedCodeByRx] = useState<Record<string, string>>({});
  const [noticeState, setNoticeState] = useState<{
    rxCode: string;
    tone: 'error' | 'success';
    message: string;
  } | null>(null);

  // BUG-P3-01: Scoped idempotencyKey per (prescriptionId, attemptId):
  // - Regenerated when switching prescriptions or after validation/conflict errors / payload edits
  // - Preserved across network/5xx retries of the exact same submit
  const attemptByPrescriptionRef = useRef<Map<string, string>>(new Map());

  const ctx = contextQuery.data;
  const prescriptionScopeKey =
    ctx?.prescriptionId != null ? String(ctx.prescriptionId) : activeRxCode;

  const getOrCreateIdempotencyKey = (scopeKey: string): string => {
    let attemptId = attemptByPrescriptionRef.current.get(scopeKey);
    if (!attemptId) {
      attemptId = createAttemptToken();
      attemptByPrescriptionRef.current.set(scopeKey, attemptId);
    }
    return `${scopeKey}:${attemptId}`;
  };

  const rotateIdempotencyAttempt = (scopeKey: string) => {
    if (!scopeKey) return;
    attemptByPrescriptionRef.current.set(scopeKey, createAttemptToken());
  };

  const isServerDispensed = ctx?.dispenseStatus === 'DISPENSED';
  const isServerCancelled = ctx?.dispenseStatus === 'CANCELLED';
  const isServerBlocked = isServerDispensed || isServerCancelled || ctx?.canDispense === false;
  const receiverIdentityKey = ctx
    ? `${activeRxCode}:${ctx.receiver.identityCard}:${ctx.receiver.receiverName}`
    : '';
  const serverChecks = ctx?.checks;
  const activeCheckEdits = activeRxCode ? checkOverridesByRx[activeRxCode] : undefined;
  const activeLineEdits = activeRxCode ? lineEditsByRx[activeRxCode] : undefined;
  const notice = noticeState?.rxCode === activeRxCode ? noticeState : null;

  const isIdentityVerified = isServerBlocked
    ? Boolean(ctx?.receiver.isIdentityVerified)
    : receiverIdentityKey && identityOverrides[receiverIdentityKey] !== undefined
      ? identityOverrides[receiverIdentityKey]
      : Boolean(ctx?.receiver.isIdentityVerified);

  const checks = useMemo<PharmacistHandoverCheck[]>(() => {
    if (!serverChecks) return [];
    if (isServerBlocked || !activeCheckEdits) {
      return serverChecks;
    }
    return serverChecks.map((check) => {
      const edited = activeCheckEdits[check.id];
      return edited === undefined ? check : { ...check, checked: edited };
    });
  }, [activeCheckEdits, isServerBlocked, serverChecks]);

  const localDispensedCode = activeRxCode ? dispensedCodeByRx[activeRxCode] : undefined;
  const dispensedCode = localDispensedCode ?? (isServerDispensed ? (ctx?.rxCode ?? null) : null);
  const isDispensed = isServerDispensed || Boolean(localDispensedCode);

  const handleToggleIdentityVerified = () => {
    if (isServerBlocked || !receiverIdentityKey) return;
    setIdentityOverrides((prev) => ({
      ...prev,
      [receiverIdentityKey]: !isIdentityVerified,
    }));
  };

  const handleToggleCheck = (checkId: string) => {
    if (isServerBlocked || !activeRxCode) return;
    const currentCheck = checks.find((c) => c.id === checkId);
    if (!currentCheck) return;
    setCheckOverridesByRx((prev) => {
      const rxChecks = prev[activeRxCode] ?? {};
      return {
        ...prev,
        [activeRxCode]: {
          ...rxChecks,
          [checkId]: !currentCheck.checked,
        },
      };
    });
  };

  const handleChangeLineQuantity = (medId: string, quantity: number) => {
    if (isServerBlocked || !activeRxCode) return;
    rotateIdempotencyAttempt(prescriptionScopeKey);
    setLineEditsByRx((prev) => {
      const rxEdits = prev[activeRxCode] ?? {};
      return {
        ...prev,
        [activeRxCode]: {
          ...rxEdits,
          [medId]: {
            ...rxEdits[medId],
            dispensedQuantity: quantity,
          },
        },
      };
    });
  };

  const handleChangeShortageReason = (medId: string, reason: string) => {
    if (isServerBlocked || !activeRxCode) return;
    rotateIdempotencyAttempt(prescriptionScopeKey);
    setLineEditsByRx((prev) => {
      const rxEdits = prev[activeRxCode] ?? {};
      return {
        ...prev,
        [activeRxCode]: {
          ...rxEdits,
          [medId]: {
            ...rxEdits[medId],
            shortageReason: reason,
          },
        },
      };
    });
  };

  const handleConfirmDispense = async () => {
    if (!ctx || isServerBlocked) return;

    if (!isIdentityVerified) {
      rotateIdempotencyAttempt(prescriptionScopeKey);
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message:
          'Bắt buộc xác nhận đối chiếu định danh CCCD của người nhận thuốc trước khi cấp phát.',
      });
      return;
    }

    const unchecked = checks.filter((c) => !c.checked);
    if (unchecked.length > 0) {
      rotateIdempotencyAttempt(prescriptionScopeKey);
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Còn ${unchecked.length} mục kiểm tra bắt buộc của Dược sĩ chưa được xác nhận.`,
      });
      return;
    }

    const itemsPayload = ctx.medications
      .filter((m) => m.prescriptionItemId != null)
      .map((m) => {
        const edit = activeLineEdits?.[m.id];
        const defaultQty = m.dispensedQuantity ?? m.prescribedQuantity ?? null;
        const resolvedQty =
          edit?.dispensedQuantity !== undefined ? edit.dispensedQuantity : defaultQty;
        const trimmedReason = edit?.shortageReason?.trim() || undefined;
        return {
          prescriptionItemId: m.prescriptionItemId as number,
          medicineBatchId: m.batchId ?? null,
          dispensedQuantity: resolvedQty,
          shortageReason: trimmedReason ?? null,
          prescribedQuantity: m.prescribedQuantity ?? 0,
          drugName: m.drugName,
        };
      });

    for (const row of itemsPayload) {
      if (row.dispensedQuantity != null && row.dispensedQuantity < 0) {
        rotateIdempotencyAttempt(prescriptionScopeKey);
        setNoticeState({
          rxCode: activeRxCode,
          tone: 'error',
          message: `Số lượng thực xuất của thuốc ${row.drugName} không được âm.`,
        });
        return;
      }
    }

    // Group by prescriptionItemId to enforce shortageReason when total dispensed < prescribed (including 0)
    const groupedByPi = new Map<
      number,
      { prescribed: number; dispensed: number; hasReason: boolean; drugName: string }
    >();
    for (const row of itemsPayload) {
      const prev = groupedByPi.get(row.prescriptionItemId);
      groupedByPi.set(row.prescriptionItemId, {
        prescribed: row.prescribedQuantity || prev?.prescribed || 0,
        dispensed: (prev?.dispensed ?? 0) + (row.dispensedQuantity ?? 0),
        hasReason: Boolean(prev?.hasReason || row.shortageReason),
        drugName: row.drugName,
      });
    }

    for (const [, summary] of groupedByPi) {
      if (summary.dispensed < summary.prescribed && !summary.hasReason) {
        rotateIdempotencyAttempt(prescriptionScopeKey);
        setNoticeState({
          rxCode: activeRxCode,
          tone: 'error',
          message: `Thuốc ${summary.drugName} có số lượng thực xuất (${summary.dispensed}) nhỏ hơn kê đơn (${summary.prescribed}). Vui lòng nhập lý do cấp thiếu thuốc.`,
        });
        return;
      }
    }

    const idempotencyKey = getOrCreateIdempotencyKey(prescriptionScopeKey);

    try {
      const result = await confirmMutation.mutateAsync({
        prescriptionIdOrCode: ctx.rxCode,
        receiverName: ctx.receiver.receiverName,
        relationship: ctx.receiver.relationship,
        receiverIdentityCard: ctx.receiver.identityCard,
        receiverPhone: ctx.receiver.phone,
        isIdentityVerified: true,
        checksConfirmed: true,
        idempotencyKey,
        items: itemsPayload.map((item) => ({
          prescriptionItemId: item.prescriptionItemId,
          medicineBatchId: item.medicineBatchId,
          dispensedQuantity: item.dispensedQuantity,
          ...(item.shortageReason ? { shortageReason: item.shortageReason } : {}),
        })),
      });

      setCheckOverridesByRx((prev) => {
        const next = { ...prev };
        delete next[activeRxCode];
        return next;
      });
      setLineEditsByRx((prev) => {
        const next = { ...prev };
        delete next[activeRxCode];
        return next;
      });
      if (receiverIdentityKey) {
        setIdentityOverrides((prev) => {
          const next = { ...prev };
          delete next[receiverIdentityKey];
          return next;
        });
      }
      setDispensedCodeByRx((prev) => ({
        ...prev,
        [activeRxCode]: result.dispenseCode,
      }));
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'success',
        message: `Đã hoàn tất bàn giao đơn thuốc ${ctx.rxCode} cho bệnh nhân ${ctx.receiver.fullName}, trừ tồn kho FEFO và phát hành biên nhận ${result.dispenseCode}.`,
      });
    } catch (error) {
      if (isValidationOrConflictError(error)) {
        rotateIdempotencyAttempt(prescriptionScopeKey);
      }
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message:
          error instanceof Error ? error.message : 'Không thể xác nhận bàn giao thuốc lúc này.',
      });
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem="dispense"
        pharmacistName={user?.fullName ?? PHARMACY_WORKBENCH_META.defaultPharmacistName}
        counterLabel={PHARMACY_WORKBENCH_META.counterLabel}
        onItemSelect={(id) => {
          if (id === 'dashboard') navigate('/pharmacy');
          if (id === 'search') navigate('/pharmacy/prescriptions');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/pharmacy/prescriptions')}
              aria-label="Quay lại hàng đợi đơn thuốc"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h1 className="flex items-center gap-1.5 truncate text-sm font-bold text-navy">
                  <PackageCheck className="h-4 w-4 text-primary" aria-hidden="true" />
                  <span>SCR-PHA-03: Xác nhận Bàn giao Thuốc &amp; Biên nhận Cấp phát</span>
                </h1>
                {ctx && (
                  <Badge tone={isDispensed ? 'emerald' : 'sky'}>
                    {isDispensed ? 'ĐÃ BÀN GIAO & ĐÓNG CA' : 'SẴN SÀNG BÀN GIAO'}
                  </Badge>
                )}
              </div>
              <p className="truncate text-[11px] text-sub">
                {ctx ? (
                  <>
                    Bàn giao đơn thuốc <span className="font-mono font-bold">{ctx.rxCode}</span> •
                    STT {ctx.ticketNo} • {ctx.receiver.fullName}
                  </>
                ) : activeRxCode ? (
                  `Đang tải đơn thuốc ${activeRxCode}`
                ) : (
                  'Chưa chọn đơn thuốc bàn giao'
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
          {!activeRxCode && (
            <div className="space-y-3 rounded-2xl border border-border bg-white p-8 text-center">
              <p className="text-sm font-bold text-navy">
                Chưa chọn đơn thuốc để xác nhận bàn giao
              </p>
              <p className="text-xs text-sub">
                Vui lòng chọn một đơn thuốc đã soạn từ Hàng đợi Nhà thuốc để đối chiếu người nhận.
              </p>
              <Button
                type="button"
                variant="primary"
                size="sm"
                onClick={() => navigate('/pharmacy/prescriptions')}
              >
                Mở Hàng đợi Đơn thuốc
              </Button>
            </div>
          )}

          {activeRxCode && contextQuery.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-8 text-center text-xs text-sub"
            >
              Đang tải thông tin người nhận và gói thuốc bàn giao...
            </div>
          )}

          {activeRxCode && !contextQuery.isPending && contextQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {contextQuery.error instanceof Error
                      ? contextQuery.error.message
                      : 'Không thể tải thông tin bàn giao đơn thuốc.'}
                  </span>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void contextQuery.refetch()}
                    className="gap-1.5"
                  >
                    <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Thử lại</span>
                  </Button>
                </div>
              </Alert>
            </div>
          )}

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone={notice.tone}>{notice.message}</Alert>
            </div>
          )}

          {ctx && (
            <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
              <div className="xl:col-span-5">
                <DispenseIdentityVerificationCard
                  context={ctx}
                  isIdentityVerified={isIdentityVerified}
                  onToggleIdentityVerified={handleToggleIdentityVerified}
                />
              </div>

              <div className="xl:col-span-7">
                <DispenseMedicationCounselingList
                  regimenBadge={ctx.regimenBadge}
                  medications={ctx.medications}
                  checks={checks}
                  onToggleCheck={handleToggleCheck}
                  lineEdits={activeLineEdits}
                  onChangeLineQuantity={handleChangeLineQuantity}
                  onChangeShortageReason={handleChangeShortageReason}
                  disabled={isDispensed || isServerBlocked || confirmMutation.isPending}
                />
              </div>
            </div>
          )}
        </main>

        {ctx && (
          <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
            <div className="flex items-center gap-2.5">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => navigate('/pharmacy/prescriptions')}
              >
                Hủy / Quay lại
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => window.print()}
                className="gap-1.5"
              >
                <Printer className="h-4 w-4" aria-hidden="true" />
                <span>In phiếu biên nhận</span>
              </Button>
              {dispensedCode && (
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() =>
                    navigate(`/pharmacy/dispensing/${encodeURIComponent(dispensedCode)}`)
                  }
                  className="gap-1.5"
                >
                  <span>Xem biên nhận {dispensedCode}</span>
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Button>
              )}
            </div>

            <Button
              variant="primary"
              size="sm"
              onClick={() => void handleConfirmDispense()}
              disabled={isDispensed || isServerBlocked || confirmMutation.isPending}
              className="gap-1.5 shadow-xs"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              <span>
                {isDispensed
                  ? 'Đã hoàn tất bàn giao & Đóng lượt (DISPENSED)'
                  : confirmMutation.isPending
                    ? 'Đang xử lý bàn giao...'
                    : 'Xác nhận Hoàn tất Bàn giao & Trừ tồn kho'}
              </span>
            </Button>
          </footer>
        )}
      </div>
    </div>
  );
}
