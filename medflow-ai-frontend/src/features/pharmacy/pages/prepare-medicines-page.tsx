import { ArrowLeft, CheckCircle2, PackageCheck, Printer, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PrepareGppChecklist } from '../components/prepare-gpp-checklist';
import { PrepareMedicineTable } from '../components/prepare-medicine-table';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import { PREPARE_MEDICINE_CONTEXT_MOCK } from '../data/prepare-medicine-mock';
import type { GppCheckItem, PrepareMedicineItem } from '../types/prepare-medicine.types';

export function PrepareMedicinesPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const ctx = PREPARE_MEDICINE_CONTEXT_MOCK;
  const activeRxCode = params.prescriptionCode?.trim() || ctx.rxCode;

  const [items, setItems] = useState<PrepareMedicineItem[]>(ctx.items);
  const [gppChecks, setGppChecks] = useState<GppCheckItem[]>(ctx.gppChecks);
  const [notice, setNotice] = useState<{
    tone: 'error' | 'info' | 'success';
    message: string;
  } | null>(null);
  const [isReadyForDispense, setIsReadyForDispense] = useState(false);

  const handlePickedQtyChange = (itemId: string, qty: number) => {
    setItems((prev) => prev.map((it) => (it.id === itemId ? { ...it, pickedQty: qty } : it)));
  };

  const handleBatchChange = (itemId: string, batchCode: string) => {
    setItems((prev) =>
      prev.map((it) => (it.id === itemId ? { ...it, selectedBatchCode: batchCode } : it)),
    );
  };

  const handleTogglePrepared = (itemId: string) => {
    setItems((prev) =>
      prev.map((it) => (it.id === itemId ? { ...it, isPrepared: !it.isPrepared } : it)),
    );
  };

  const handleToggleCheck = (checkId: string) => {
    setGppChecks((prev) => prev.map((c) => (c.id === checkId ? { ...c, checked: !c.checked } : c)));
  };

  const handleConfirmPreparation = () => {
    const unprepared = items.filter((i) => !i.isPrepared);
    const uncheckedGpp = gppChecks.filter((c) => !c.checked);
    const mismatchedQty = items.filter((i) => i.pickedQty !== i.prescribedQty);

    if (mismatchedQty.length > 0) {
      setNotice({
        tone: 'error',
        message: `Số lượng thực soạn chưa khớp y lệnh tại ${mismatchedQty.length} khoản thuốc. Vui lòng kiểm tra lại.`,
      });
      return;
    }

    if (unprepared.length > 0) {
      setNotice({
        tone: 'error',
        message: `Còn ${unprepared.length} khoản thuốc chưa được đánh dấu "Đã soạn đủ".`,
      });
      return;
    }

    if (uncheckedGpp.length > 0) {
      setNotice({
        tone: 'error',
        message: `Vui lòng hoàn tất toàn bộ ${uncheckedGpp.length} tiêu chuẩn kiểm soát GPP còn lại trước khi xác nhận.`,
      });
      return;
    }

    setIsReadyForDispense(true);
    setNotice({
      tone: 'success',
      message: `Đã hoàn tất soạn đơn ${activeRxCode} theo nguyên tắc FEFO/GPP và chuyển sang quầy phát thuốc!`,
    });
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
                <PackageCheck className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Bàn làm việc Soạn thuốc &amp; Kiểm tra Lô hạn dùng (SCR-PHA-02)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                Đơn thuốc <span className="font-mono font-bold text-primary">{activeRxCode}</span> •{' '}
                {ctx.doctorName} ({ctx.roomLabel})
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
          {/* Patient & Prescription Summary Card */}
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
                  <strong className="text-primary">{activeRxCode}</strong> • Lượt khám:{' '}
                  <strong className="text-navy">{ctx.visitCode}</strong>
                </p>
                <p className="text-xs text-ink">
                  <strong className="text-navy">Chẩn đoán xác định:</strong> {ctx.primaryDiagnosis}{' '}
                  • {ctx.secondaryDiagnosis}
                </p>
              </div>

              <div className="space-y-2 text-right">
                <Badge tone="emerald">{ctx.paymentBadge}</Badge>
                <div className="text-[11px] text-sub">
                  BS kê đơn: <strong className="text-navy">{ctx.doctorName}</strong>
                </div>
              </div>
            </div>

            <div
              role="note"
              className="flex items-center gap-2 rounded-xl border border-rose-300 bg-rose-50 px-4 py-2.5 text-xs font-bold text-rose-700"
            >
              <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span>{ctx.criticalAllergy}</span>
            </div>
          </section>

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone={notice.tone}>{notice.message}</Alert>
            </div>
          )}

          {/* Medicine Preparation Table */}
          <PrepareMedicineTable
            items={items}
            onPickedQtyChange={handlePickedQtyChange}
            onBatchChange={handleBatchChange}
            onTogglePrepared={handleTogglePrepared}
          />

          {/* GPP Quality Check Checklist */}
          <PrepareGppChecklist checks={gppChecks} onToggleCheck={handleToggleCheck} />
        </main>

        {/* Sticky Footer Bar */}
        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
          <div className="flex flex-wrap items-center gap-3">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => navigate('/pharmacy')}
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
              • Tổng <strong className="font-mono text-navy">{totalPickedUnits} viên</strong>
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

            <Button
              type="button"
              variant="primary"
              size="sm"
              onClick={handleConfirmPreparation}
              className="gap-1.5 shadow-xs"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              <span>Xác nhận Đã soạn xong &amp; Chuyển Phát thuốc</span>
            </Button>
          </div>
        </footer>
      </div>
    </div>
  );
}
