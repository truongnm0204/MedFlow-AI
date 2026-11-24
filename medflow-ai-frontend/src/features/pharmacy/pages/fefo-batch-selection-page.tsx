import { ArrowLeft, CheckCircle2, Layers, RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { FefoBatchSelectorCard } from '../components/fefo-batch-selector-card';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  usePreparePrescriptionContext,
  useSubmitPreparePrescription,
} from '../hooks/use-pharmacy-workbench';
import type {
  BatchStatus,
  FefoBatchRecord,
  MedicineBatchRequirement,
} from '../types/fefo-batch.types';
import type { PreparePrescriptionContext } from '../types/prepare-medicine.types';

function computeDaysUntilExpiry(expiryText: string): number {
  const parts = expiryText.split('/');
  const parsed =
    parts.length === 3
      ? new Date(`${parts[2]}-${parts[1]}-${parts[0]}T00:00:00Z`)
      : new Date(expiryText);
  if (Number.isNaN(parsed.getTime())) return 90;
  return Math.floor((parsed.getTime() - Date.now()) / (1000 * 60 * 60 * 24));
}

function mapContextToRequirements(ctx: PreparePrescriptionContext): MedicineBatchRequirement[] {
  return ctx.items.map((item) => {
    const batches: FefoBatchRecord[] = item.availableBatches.map((b) => {
      const days = computeDaysUntilExpiry(b.expiryDate);
      const isExpired = days < 0;
      const isOutOfStock = b.availableStock <= 0;
      let status: BatchStatus = 'AVAILABLE';
      let blockReason: string | undefined;

      if (isExpired) {
        status = 'EXPIRED';
        blockReason = 'Lô đã quá hạn sử dụng theo kiểm định GPP — Hệ thống khóa tự động';
      } else if (isOutOfStock) {
        status = 'BLOCKED';
        blockReason = 'Lô không còn tồn kho khả dụng';
      } else if (b.isFefoRecommended) {
        status = 'FEFO_RECOMMENDED';
      }

      const id = b.batchId != null ? String(b.batchId) : b.batchCode;
      return {
        id,
        batchCode: b.batchCode,
        drugName: item.drugName,
        manufacturer: 'Kho Dược Bệnh viện',
        expiryDate: b.expiryDate,
        daysUntilExpiry: days,
        availableQty: b.availableStock,
        unit: item.unit,
        shelfLocation: b.shelfLocation,
        status,
        blockReason,
      };
    });

    const selectedBatch =
      batches.find(
        (b) =>
          (item.selectedBatchId != null && b.id === String(item.selectedBatchId)) ||
          (item.selectedBatchCode && b.batchCode === item.selectedBatchCode),
      ) ?? batches.find((b) => b.status === 'FEFO_RECOMMENDED');

    return {
      id: item.id,
      drugName: item.drugName,
      dosageForm: item.dosageForm,
      requiredQty: item.prescribedQty,
      unit: item.unit,
      selectedBatchId: selectedBatch?.id ?? null,
      batches,
    };
  });
}

export function FefoBatchSelectionPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeRxCode = params.prescriptionCode?.trim() || searchParams.get('rxCode')?.trim() || '';

  const contextQuery = usePreparePrescriptionContext(activeRxCode);
  const submitMutation = useSubmitPreparePrescription(activeRxCode);

  const [requirements, setRequirements] = useState<MedicineBatchRequirement[]>([]);
  const [notice, setNotice] = useState<{
    tone: 'info' | 'success' | 'error';
    message: string;
  } | null>(null);

  useEffect(() => {
    if (contextQuery.data) {
      setRequirements(mapContextToRequirements(contextQuery.data));
    }
  }, [contextQuery.data]);

  const handleSelectBatch = (requirementId: string, batch: FefoBatchRecord) => {
    if (batch.status === 'EXPIRED' || batch.status === 'BLOCKED') {
      setNotice({
        tone: 'error',
        message: `Không thể chọn lô ${batch.batchCode}: ${batch.blockReason ?? 'Lô bị khóa'}.`,
      });
      return;
    }

    setRequirements((prev) =>
      prev.map((req) => (req.id === requirementId ? { ...req, selectedBatchId: batch.id } : req)),
    );
    setNotice({
      tone: 'info',
      message: `Đã chọn lô ${batch.batchCode} (HSD ${batch.expiryDate}) cho ${batch.drugName}.`,
    });
  };

  const handleSaveBatches = async () => {
    const missing = requirements.filter((r) => !r.selectedBatchId);
    if (missing.length > 0) {
      setNotice({
        tone: 'error',
        message: `Còn ${missing.length} loại thuốc chưa chọn lô xuất kho hợp lệ.`,
      });
      return;
    }

    try {
      await submitMutation.mutateAsync({
        items: requirements.map((req) => {
          const chosen = req.batches.find((b) => b.id === req.selectedBatchId);
          const numericBatchId = chosen && /^\d+$/.test(chosen.id) ? Number(chosen.id) : null;
          return {
            prescriptionItemId: Number(req.id),
            medicineBatchId: numericBatchId,
            batchCode: chosen?.batchCode ?? null,
            pickedQuantity: req.requiredQty,
          };
        }),
      });
      setNotice({
        tone: 'success',
        message: 'Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho toàn bộ đơn thuốc.',
      });
    } catch (error) {
      setNotice({
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
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
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
              disabled={submitMutation.isPending}
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
