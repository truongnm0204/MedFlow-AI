import { ArrowLeft, CheckCircle2, PackageCheck, Printer } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { DispenseIdentityVerificationCard } from '../components/dispense-identity-verification-card';
import { DispenseMedicationCounselingList } from '../components/dispense-medication-counseling-list';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { DISPENSE_CONFIRM_CONTEXT_MOCK } from '../data/dispense-confirm-mock';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import type { PharmacistHandoverCheck } from '../types/dispense-confirm.types';

export function DispenseConfirmPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const ctx = DISPENSE_CONFIRM_CONTEXT_MOCK;
  const activeRxCode = params.prescriptionCode?.trim() || ctx.rxCode;

  const [isIdentityVerified, setIsIdentityVerified] = useState(ctx.receiver.isIdentityVerified);
  const [checks, setChecks] = useState<PharmacistHandoverCheck[]>(ctx.checks);
  const [notice, setNotice] = useState<{ tone: 'error' | 'success'; message: string } | null>(null);
  const [isDispensed, setIsDispensed] = useState(false);

  const handleToggleCheck = (checkId: string) => {
    setChecks((prev) => prev.map((c) => (c.id === checkId ? { ...c, checked: !c.checked } : c)));
  };

  const handleConfirmDispense = () => {
    if (!isIdentityVerified) {
      setNotice({
        tone: 'error',
        message:
          'Bắt buộc xác nhận đối chiếu định danh CCCD của người nhận thuốc trước khi cấp phát.',
      });
      return;
    }

    const unchecked = checks.filter((c) => !c.checked);
    if (unchecked.length > 0) {
      setNotice({
        tone: 'error',
        message: `Còn ${unchecked.length} mục kiểm tra bắt buộc của Dược sĩ chưa được xác nhận.`,
      });
      return;
    }

    setIsDispensed(true);
    setNotice({
      tone: 'success',
      message: `Đã hoàn tất bàn giao đơn thuốc ${activeRxCode} cho bệnh nhân ${ctx.receiver.fullName}, trừ tồn kho FEFO và phát hành biên nhận DISP-2026-0415-0042.`,
    });
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem="dispense"
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
              <div className="flex items-center gap-2">
                <h1 className="truncate text-sm font-bold text-navy flex items-center gap-1.5">
                  <PackageCheck className="h-4 w-4 text-primary" aria-hidden="true" />
                  <span>SCR-PHA-03: Xác nhận Bàn giao Thuốc &amp; Biên nhận Cấp phát</span>
                </h1>
                <Badge tone={isDispensed ? 'emerald' : 'sky'}>
                  {isDispensed ? 'ĐÃ BÀN GIAO & ĐÓNG CA' : 'SẴN SÀNG BÀN GIAO'}
                </Badge>
              </div>
              <p className="truncate text-[11px] text-sub">
                Bàn giao đơn thuốc <span className="font-mono font-bold">{activeRxCode}</span> • STT{' '}
                {ctx.ticketNo} • {ctx.receiver.fullName}
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
          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone={notice.tone}>{notice.message}</Alert>
            </div>
          )}

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
            <div className="xl:col-span-5">
              <DispenseIdentityVerificationCard
                context={{ ...ctx, rxCode: activeRxCode }}
                isIdentityVerified={isIdentityVerified}
                onToggleIdentityVerified={() => setIsIdentityVerified((prev) => !prev)}
              />
            </div>

            <div className="xl:col-span-7">
              <DispenseMedicationCounselingList
                regimenBadge={ctx.regimenBadge}
                medications={ctx.medications}
                checks={checks}
                onToggleCheck={handleToggleCheck}
              />
            </div>
          </div>
        </main>

        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
          <div className="flex items-center gap-2.5">
            <Button variant="secondary" size="sm" onClick={() => navigate('/pharmacy')}>
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
          </div>

          <Button
            variant="primary"
            size="sm"
            onClick={handleConfirmDispense}
            disabled={isDispensed}
            className="gap-1.5 shadow-xs"
          >
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>
              {isDispensed
                ? 'Đã hoàn tất bàn giao & Đóng lượt (DISPENSED)'
                : 'Xác nhận Hoàn tất Bàn giao & Trừ tồn kho'}
            </span>
          </Button>
        </footer>
      </div>
    </div>
  );
}
