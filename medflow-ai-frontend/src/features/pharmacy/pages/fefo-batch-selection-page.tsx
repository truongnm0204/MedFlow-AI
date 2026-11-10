import { ArrowLeft, CheckCircle2, Layers } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { FefoBatchSelectorCard } from '../components/fefo-batch-selector-card';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { FEFO_REQUIREMENTS_MOCK } from '../data/fefo-batch-mock';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import type { FefoBatchRecord, MedicineBatchRequirement } from '../types/fefo-batch.types';

export function FefoBatchSelectionPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [requirements, setRequirements] =
    useState<MedicineBatchRequirement[]>(FEFO_REQUIREMENTS_MOCK);
  const [notice, setNotice] = useState<{
    tone: 'info' | 'success' | 'error';
    message: string;
  } | null>(null);

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

  const handleSaveBatches = () => {
    const missing = requirements.filter((r) => !r.selectedBatchId);
    if (missing.length > 0) {
      setNotice({
        tone: 'error',
        message: `Còn ${missing.length} loại thuốc chưa chọn lô xuất kho hợp lệ.`,
      });
      return;
    }

    setNotice({
      tone: 'success',
      message: 'Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho toàn bộ đơn thuốc.',
    });
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem="prepare"
        pharmacistName={user?.fullName ?? PHARMACY_WORKBENCH_META.defaultPharmacistName}
        counterLabel={PHARMACY_WORKBENCH_META.counterLabel}
        onItemSelect={(id) => {
          if (id === 'dashboard') navigate('/pharmacy');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/pharmacy')}
              aria-label="Quay lại tổng quan nhà thuốc"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy flex items-center gap-2">
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
          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone={notice.tone}>{notice.message}</Alert>
            </div>
          )}

          <div className="space-y-4">
            {requirements.map((req) => (
              <FefoBatchSelectorCard
                key={req.id}
                requirement={req}
                onSelectBatch={handleSelectBatch}
              />
            ))}
          </div>
        </main>

        <footer className="sticky bottom-0 z-10 flex items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm">
          <Button variant="secondary" size="sm" onClick={() => navigate('/pharmacy')}>
            Hủy bỏ
          </Button>
          <Button variant="primary" size="sm" onClick={handleSaveBatches} className="gap-1.5">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>Xác nhận chọn lô FEFO</span>
          </Button>
        </footer>
      </div>
    </div>
  );
}
