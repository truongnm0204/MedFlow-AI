import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  PackageCheck,
  Printer,
  RefreshCw,
  ShieldAlert,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PrepareGppChecklist } from '../components/prepare-gpp-checklist';
import { PrepareMedicineTable } from '../components/prepare-medicine-table';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  usePreparePrescriptionContext,
  useSubmitPreparePrescription,
} from '../hooks/use-pharmacy-workbench';
import type { GppCheckItem, PrepareMedicineItem } from '../types/prepare-medicine.types';

interface PrepareItemDraftEdit {
  pickedQty?: number;
  selectedBatchCode?: string;
  isPrepared?: boolean;
  basePrescribedQty: number;
}

interface PrepareRxDraft {
  items: Record<string, PrepareItemDraftEdit>;
  checks: Record<string, boolean>;
}

export function PrepareMedicinesPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeRxCode = params.prescriptionCode?.trim() || searchParams.get('rxCode')?.trim() || '';

  const contextQuery = usePreparePrescriptionContext(activeRxCode);
  const submitMutation = useSubmitPreparePrescription(activeRxCode);

  const [draftsByRx, setDraftsByRx] = useState<Record<string, PrepareRxDraft>>({});
  const [confirmedStatusAtByRx, setConfirmedStatusAtByRx] = useState<Record<string, string>>({});
  const [noticeState, setNoticeState] = useState<{
    rxCode: string;
    tone: 'error' | 'info' | 'success';
    message: string;
  } | null>(null);

  const ctx = contextQuery.data;
  const serverItems = ctx?.items;
  const serverGppChecks = ctx?.gppChecks;
  const serverStatus = ctx?.status;
  const isLockedStatus = serverStatus === 'DISPENSED' || serverStatus === 'CANCELLED';
  const activeDraft = activeRxCode ? draftsByRx[activeRxCode] : undefined;
  const notice = noticeState?.rxCode === activeRxCode ? noticeState : null;

  const items = useMemo<PrepareMedicineItem[]>(() => {
    if (!serverItems) return [];
    if (isLockedStatus || !activeDraft) {
      return serverItems;
    }

    return serverItems.map((serverItem) => {
      const edit = activeDraft.items[String(serverItem.id)];
      if (!edit || edit.basePrescribedQty !== serverItem.prescribedQty) {
        return serverItem;
      }

      const nextPickedQty = edit.pickedQty ?? serverItem.pickedQty;
      const targetBatchCode = edit.selectedBatchCode ?? serverItem.selectedBatchCode;
      const matchedBatch = targetBatchCode
        ? serverItem.availableBatches.find((b) => b.batchCode === targetBatchCode)
        : undefined;

      if (!matchedBatch || matchedBatch.availableStock < nextPickedQty) {
        return {
          ...serverItem,
          pickedQty: nextPickedQty,
          isPrepared: false,
        };
      }

      return {
        ...serverItem,
        pickedQty: nextPickedQty,
        selectedBatchCode: targetBatchCode,
        selectedBatchId:
          edit.selectedBatchCode !== undefined
            ? (matchedBatch.batchId ?? null)
            : serverItem.selectedBatchId,
        isPrepared: edit.isPrepared ?? serverItem.isPrepared,
      };
    });
  }, [activeDraft, isLockedStatus, serverItems]);

  const gppChecks = useMemo<GppCheckItem[]>(() => {
    if (!serverGppChecks) return [];
    if (isLockedStatus || !activeDraft) {
      return serverGppChecks;
    }
    return serverGppChecks.map((check) => {
      const editedChecked = activeDraft.checks[check.id];
      return editedChecked === undefined ? check : { ...check, checked: editedChecked };
    });
  }, [activeDraft, isLockedStatus, serverGppChecks]);

  const isReadyForDispense =
    serverStatus === 'PREPARED' ||
    serverStatus === 'READY_FOR_DISPENSE' ||
    (!isLockedStatus &&
      Boolean(activeRxCode) &&
      confirmedStatusAtByRx[activeRxCode] !== undefined &&
      confirmedStatusAtByRx[activeRxCode] === (serverStatus ?? ''));

  const updateItemDraft = (
    itemId: string,
    updater: (
      current: PrepareMedicineItem,
      prevEdit?: PrepareItemDraftEdit,
    ) => PrepareItemDraftEdit,
  ) => {
    if (!activeRxCode || isLockedStatus) return;
    const currentItem = items.find((it) => String(it.id) === String(itemId));
    if (!currentItem) return;
    const key = String(currentItem.id);

    setDraftsByRx((prev) => {
      const rxDraft = prev[activeRxCode] ?? { items: {}, checks: {} };
      return {
        ...prev,
        [activeRxCode]: {
          ...rxDraft,
          items: {
            ...rxDraft.items,
            [key]: updater(currentItem, rxDraft.items[key]),
          },
        },
      };
    });
  };

  const handlePickedQtyChange = (itemId: string, qty: number) => {
    updateItemDraft(itemId, (current, prevEdit) => ({
      ...prevEdit,
      pickedQty: qty,
      basePrescribedQty: current.prescribedQty,
    }));
  };

  const handleBatchChange = (itemId: string, batchCode: string) => {
    updateItemDraft(itemId, (current, prevEdit) => ({
      ...prevEdit,
      selectedBatchCode: batchCode,
      basePrescribedQty: current.prescribedQty,
    }));
  };

  const handleTogglePrepared = (itemId: string) => {
    updateItemDraft(itemId, (current, prevEdit) => ({
      ...prevEdit,
      selectedBatchCode: current.selectedBatchCode,
      isPrepared: !current.isPrepared,
      basePrescribedQty: current.prescribedQty,
    }));
  };

  const handleToggleCheck = (checkId: string) => {
    if (!activeRxCode || isLockedStatus) return;
    const currentCheck = gppChecks.find((c) => c.id === checkId);
    if (!currentCheck) return;

    setDraftsByRx((prev) => {
      const rxDraft = prev[activeRxCode] ?? { items: {}, checks: {} };
      return {
        ...prev,
        [activeRxCode]: {
          ...rxDraft,
          checks: {
            ...rxDraft.checks,
            [checkId]: !currentCheck.checked,
          },
        },
      };
    });
  };

  const handleConfirmPreparation = async () => {
    if (isLockedStatus) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: 'Đơn thuốc này đã đóng hoặc đã cấp phát, không thể cập nhật soạn thuốc.',
      });
      return;
    }

    const unprepared = items.filter((i) => !i.isPrepared);
    const uncheckedGpp = gppChecks.filter((c) => !c.checked);
    const mismatchedQty = items.filter((i) => i.pickedQty !== i.prescribedQty);

    if (mismatchedQty.length > 0) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Số lượng thực soạn chưa khớp y lệnh tại ${mismatchedQty.length} khoản thuốc. Vui lòng kiểm tra lại.`,
      });
      return;
    }

    if (unprepared.length > 0) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Còn ${unprepared.length} khoản thuốc chưa được đánh dấu "Đã soạn đủ".`,
      });
      return;
    }

    if (uncheckedGpp.length > 0) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Vui lòng hoàn tất toàn bộ ${uncheckedGpp.length} tiêu chuẩn kiểm soát GPP còn lại trước khi xác nhận.`,
      });
      return;
    }

    try {
      const response = await submitMutation.mutateAsync({
        items: items.map((item) => ({
          prescriptionItemId: item.prescriptionItemId ?? Number(item.id),
          medicineBatchId: item.selectedBatchId ?? null,
          batchCode: item.selectedBatchCode || null,
          pickedQuantity: item.pickedQty,
        })),
      });

      setDraftsByRx((prev) => {
        const next = { ...prev };
        delete next[activeRxCode];
        return next;
      });
      setConfirmedStatusAtByRx((prev) => ({
        ...prev,
        [activeRxCode]: serverStatus ?? '',
      }));
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'success',
        message:
          response.message ||
          `Đã hoàn tất soạn đơn ${activeRxCode} theo nguyên tắc FEFO/GPP và chuyển sang quầy phát thuốc!`,
      });
    } catch (error) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message:
          error instanceof Error ? error.message : 'Không thể lưu kết quả soạn thuốc lên máy chủ.',
      });
    }
  };

  const totalPickedUnits = items.reduce((sum, i) => sum + i.pickedQty, 0);
  const preparedCount = items.filter((i) => i.isPrepared).length;

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem="prepare"
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
              <h1 className="flex items-center gap-2 truncate text-sm font-bold text-navy">
                <PackageCheck className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Bàn làm việc Soạn thuốc &amp; Kiểm tra Lô hạn dùng (SCR-PHA-02)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                {activeRxCode ? (
                  <>
                    Đơn thuốc{' '}
                    <span className="font-mono font-bold text-primary">{activeRxCode}</span>
                    {ctx ? ` • ${ctx.doctorName} (${ctx.roomLabel})` : ''}
                  </>
                ) : (
                  'Chưa chọn đơn thuốc'
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
            <div className="rounded-2xl border border-border bg-white p-8 text-center space-y-3">
              <p className="text-sm font-bold text-navy">
                Chưa chọn đơn thuốc cần chuẩn bị &amp; kiểm tra lô FEFO
              </p>
              <p className="text-xs text-sub">
                Vui lòng chọn một đơn thuốc từ Hàng đợi Nhà thuốc để tải dữ liệu y lệnh thực tế.
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
              Đang tải thông tin đơn thuốc và tồn kho lô FEFO...
            </div>
          )}

          {activeRxCode && !contextQuery.isPending && contextQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {contextQuery.error instanceof Error
                      ? contextQuery.error.message
                      : 'Không thể tải ngữ cảnh soạn thuốc.'}
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

          {activeRxCode && ctx && (
            <>
              <section
                aria-label="Thông tin bệnh nhân và đơn thuốc"
                className="rounded-2xl border border-border bg-white p-5 shadow-2xs space-y-4"
              >
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="space-y-1">
                    <div className="flex flex-wrap items-center gap-2.5">
                      <p className="text-base font-bold text-navy">{ctx.patientName}</p>
                      <span className="text-xs text-sub">
                        • {ctx.gender}, {ctx.ageLabel}
                      </span>
                      <Badge tone={isReadyForDispense ? 'emerald' : 'sky'}>
                        {isReadyForDispense ? 'Đã soạn xong • Chờ phát' : 'Đang soạn thuốc'}
                      </Badge>
                    </div>
                    <p className="font-mono text-xs text-sub">
                      Mã BN: <strong className="text-navy">{ctx.patientCode}</strong> • Mã đơn:{' '}
                      <strong className="text-primary">{ctx.rxCode}</strong> • Lượt khám:{' '}
                      <strong className="text-navy">{ctx.visitCode}</strong>
                    </p>
                    <p className="text-xs text-ink">
                      <strong className="text-navy">Chẩn đoán xác định:</strong>{' '}
                      {ctx.primaryDiagnosis}
                      {ctx.secondaryDiagnosis ? ` • ${ctx.secondaryDiagnosis}` : ''}
                    </p>
                  </div>

                  <div className="space-y-2 text-right">
                    <Badge tone="emerald">{ctx.paymentBadge}</Badge>
                    <div className="text-[11px] text-sub">
                      BS kê đơn: <strong className="text-navy">{ctx.doctorName}</strong>
                    </div>
                  </div>
                </div>

                {ctx.criticalAllergy && (
                  <div
                    role="note"
                    className="flex items-center gap-2 rounded-xl border border-rose-300 bg-rose-50 px-4 py-2.5 text-xs font-bold text-rose-700"
                  >
                    <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>{ctx.criticalAllergy}</span>
                  </div>
                )}
              </section>

              {notice && (
                <div role="status" aria-live="polite">
                  <Alert tone={notice.tone}>{notice.message}</Alert>
                </div>
              )}

              <PrepareMedicineTable
                items={items}
                onPickedQtyChange={handlePickedQtyChange}
                onBatchChange={handleBatchChange}
                onTogglePrepared={handleTogglePrepared}
              />

              <PrepareGppChecklist checks={gppChecks} onToggleCheck={handleToggleCheck} />
            </>
          )}
        </main>

        {ctx && (
          <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => navigate('/pharmacy/prescriptions')}
                className="gap-1.5"
              >
                <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                <span>Quay lại hàng đợi</span>
              </Button>

              <span className="text-xs font-medium text-sub">
                Tiến độ:{' '}
                <strong className="text-navy">
                  {preparedCount}/{items.length} khoản
                </strong>{' '}
                • Tổng <strong className="font-mono text-navy">{totalPickedUnits} đơn vị</strong>
              </span>
            </div>

            <div className="flex flex-wrap items-center gap-2.5">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => window.print()}
                className="gap-1.5"
              >
                <Printer className="h-4 w-4" aria-hidden="true" />
                <span>In nhãn hướng dẫn uống thuốc</span>
              </Button>

              {isReadyForDispense && (
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() =>
                    navigate(`/pharmacy/prescriptions/${encodeURIComponent(ctx.rxCode)}/dispense`)
                  }
                  className="gap-1.5"
                >
                  <span>Mở màn hình Bàn giao</span>
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Button>
              )}

              <Button
                type="button"
                variant="primary"
                size="sm"
                disabled={isLockedStatus || submitMutation.isPending}
                onClick={() => void handleConfirmPreparation()}
                className="gap-1.5 shadow-xs"
              >
                <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                <span>
                  {submitMutation.isPending
                    ? 'Đang xác nhận...'
                    : 'Xác nhận Đã soạn xong & Chuyển Phát thuốc'}
                </span>
              </Button>
            </div>
          </footer>
        )}
      </div>
    </div>
  );
}
