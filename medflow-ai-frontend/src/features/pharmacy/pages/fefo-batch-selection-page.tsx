import { ArrowLeft, CheckCircle2, Layers, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { FefoBatchSelectorCard } from '../components/fefo-batch-selector-card';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  useFefoBatchSelectionContext,
  useSubmitFefoBatchSelection,
} from '../hooks/use-pharmacy-workbench';
import {
  isRequirementFefoOverride,
  type FefoBatchRecord,
  type MedicineBatchRequirement,
} from '../types/fefo-batch.types';

interface RequirementDraftEdit {
  selectedBatchId?: string;
  overrideReason?: string;
}

export function FefoBatchSelectionPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeRxCode = params.prescriptionCode?.trim() || searchParams.get('rxCode')?.trim() || '';

  const contextQuery = useFefoBatchSelectionContext(activeRxCode);
  const submitMutation = useSubmitFefoBatchSelection(activeRxCode);

  const [editsByRx, setEditsByRx] = useState<Record<string, Record<string, RequirementDraftEdit>>>(
    {},
  );
  const [noticeState, setNoticeState] = useState<{
    rxCode: string;
    tone: 'info' | 'success' | 'error';
    message: string;
  } | null>(null);

  const serverRequirements = contextQuery.data?.items;
  const serverStatus = contextQuery.data?.status;
  const isLockedStatus = serverStatus === 'DISPENSED' || serverStatus === 'CANCELLED';
  const activeEdits = activeRxCode ? editsByRx[activeRxCode] : undefined;
  const notice = noticeState?.rxCode === activeRxCode ? noticeState : null;

  const requirements = useMemo<MedicineBatchRequirement[]>(() => {
    if (!serverRequirements) return [];
    if (isLockedStatus || !activeEdits) {
      return serverRequirements;
    }

    return serverRequirements.map((req) => {
      const edit = activeEdits[String(req.id)];
      if (!edit) return req;

      if (edit.selectedBatchId !== undefined) {
        const chosenBatch = req.batches.find((b) => b.id === edit.selectedBatchId);
        const isBatchStillValid =
          chosenBatch !== undefined &&
          chosenBatch.status !== 'EXPIRED' &&
          chosenBatch.status !== 'BLOCKED' &&
          chosenBatch.isSelectable !== false &&
          chosenBatch.availableQty >= req.requiredQty;

        if (!isBatchStillValid) {
          return req;
        }

        return {
          ...req,
          selectedBatchId: edit.selectedBatchId,
          overrideReason: edit.overrideReason ?? req.overrideReason,
        };
      }

      return edit.overrideReason !== undefined
        ? { ...req, overrideReason: edit.overrideReason }
        : req;
    });
  }, [activeEdits, isLockedStatus, serverRequirements]);

  const handleSelectBatch = (requirementId: string, batch: FefoBatchRecord) => {
    if (isLockedStatus || !activeRxCode) return;

    if (batch.status === 'EXPIRED' || batch.status === 'BLOCKED' || batch.isSelectable === false) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Không thể chọn lô ${batch.batchCode}: ${batch.blockReason ?? 'Lô bị khóa'}.`,
      });
      return;
    }

    const reqKey = String(requirementId);
    setEditsByRx((prev) => {
      const rxEdits = prev[activeRxCode] ?? {};
      return {
        ...prev,
        [activeRxCode]: {
          ...rxEdits,
          [reqKey]: {
            ...rxEdits[reqKey],
            selectedBatchId: batch.id,
          },
        },
      };
    });
    setNoticeState({
      rxCode: activeRxCode,
      tone: 'info',
      message: `Đã chọn lô ${batch.batchCode} (HSD ${batch.expiryDate}) cho ${batch.drugName}.`,
    });
  };

  const handleOverrideReasonChange = (requirementId: string, reason: string) => {
    if (isLockedStatus || !activeRxCode) return;
    const reqKey = String(requirementId);
    setEditsByRx((prev) => {
      const rxEdits = prev[activeRxCode] ?? {};
      return {
        ...prev,
        [activeRxCode]: {
          ...rxEdits,
          [reqKey]: {
            ...rxEdits[reqKey],
            overrideReason: reason,
          },
        },
      };
    });
  };

  const handleSaveBatches = async () => {
    if (isLockedStatus) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: 'Đơn thuốc đã đóng hoặc đã cấp phát, không thể đổi lô FEFO.',
      });
      return;
    }

    const missing = requirements.filter((r) => !r.selectedBatchId);
    if (missing.length > 0) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Còn ${missing.length} loại thuốc chưa chọn lô xuất kho hợp lệ.`,
      });
      return;
    }

    const missingOverrideReason = requirements.find(
      (req) => isRequirementFefoOverride(req) && (req.overrideReason?.trim().length ?? 0) < 5,
    );
    if (missingOverrideReason) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message: `Vui lòng nhập lý do hợp lệ (tối thiểu 5 ký tự) cho ${missingOverrideReason.drugName} khi chọn lô có hạn dùng xa hơn lô ưu tiên FEFO.`,
      });
      return;
    }

    try {
      const response = await submitMutation.mutateAsync({
        items: requirements.map((req) => {
          const chosen = req.batches.find((b) => b.id === req.selectedBatchId);
          const numericBatchId = chosen && /^\d+$/.test(chosen.id) ? Number(chosen.id) : null;
          const trimmedReason = req.overrideReason?.trim() || null;
          return {
            prescriptionItemId: Number(req.id),
            medicineBatchId: numericBatchId,
            batchCode: chosen?.batchCode ?? null,
            pickedQuantity: req.requiredQty,
            overrideReason: isRequirementFefoOverride(req) ? trimmedReason : null,
          };
        }),
      });
      setEditsByRx((prev) => {
        const next = { ...prev };
        delete next[activeRxCode];
        return next;
      });
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'success',
        message:
          response.message ||
          'Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho toàn bộ đơn thuốc.',
      });
    } catch (error) {
      setNoticeState({
        rxCode: activeRxCode,
        tone: 'error',
        message:
          error instanceof Error ? error.message : 'Không thể lưu phân bổ lô FEFO lên máy chủ.',
      });
    }
  };

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
                <Layers className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Chọn Lô Thuốc Xuất Kho theo Nguyên Tắc FEFO</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                Ưu tiên xuất lô còn hạn sử dụng gần nhất • Chặn tuyệt đối lô hết hạn hoặc niêm phong
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-60"
          >
            Đăng xuất
          </button>
        </header>

        <main className="flex-1 space-y-5 overflow-y-auto p-6 pb-24">
          {!activeRxCode && (
            <div className="rounded-2xl border border-border bg-white p-8 text-center space-y-3">
              <p className="text-sm font-bold text-navy">
                Chưa chọn đơn thuốc để phân bổ lô xuất kho FEFO
              </p>
              <p className="text-xs text-sub">
                Vui lòng chọn đơn thuốc từ Hàng đợi Nhà thuốc để tải tồn kho thực tế theo từng lô.
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
              Đang tải dữ liệu tồn kho lô FEFO...
            </div>
          )}

          {activeRxCode && !contextQuery.isPending && contextQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {contextQuery.error instanceof Error
                      ? contextQuery.error.message
                      : 'Không thể tải danh sách lô thuốc FEFO.'}
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

          {activeRxCode && contextQuery.data && (
            <div className="space-y-4">
              {requirements.map((req) => (
                <FefoBatchSelectorCard
                  key={req.id}
                  requirement={req}
                  onSelectBatch={handleSelectBatch}
                  onOverrideReasonChange={handleOverrideReasonChange}
                />
              ))}
            </div>
          )}
        </main>

        {activeRxCode && contextQuery.data && (
          <footer className="sticky bottom-0 z-10 flex items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate('/pharmacy/prescriptions')}
            >
              Hủy bỏ
            </Button>
            <Button
              variant="primary"
              size="sm"
              disabled={isLockedStatus || submitMutation.isPending}
              onClick={() => void handleSaveBatches()}
              className="gap-1.5"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              <span>{submitMutation.isPending ? 'Đang lưu...' : 'Xác nhận chọn lô FEFO'}</span>
            </Button>
          </footer>
        )}
      </div>
    </div>
  );
}
