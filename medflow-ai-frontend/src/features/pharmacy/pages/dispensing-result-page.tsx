import { ArrowLeft, FileDown, PackageCheck, Printer } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { DispensingReceiptCard } from '../components/dispensing-receipt-card';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { DISPENSING_RESULT_MOCK } from '../data/dispensing-result-mock';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';

export function DispensingResultPage() {
  const params = useParams<{ dispenseCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const detail = DISPENSING_RESULT_MOCK;
  const activeDispenseCode = params.dispenseCode?.trim() || detail.dispenseCode;
  const [notice, setNotice] = useState<string | null>(null);

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
              <h1 className="truncate text-sm font-bold text-navy flex items-center gap-2">
                <PackageCheck className="h-4 w-4 text-emerald-600" aria-hidden="true" />
                <span>Kết quả Bàn giao Thuốc &amp; Biên nhận Cấp phát (SCR-PHA-04)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                Mã bàn giao: <span className="font-mono font-bold">{activeDispenseCode}</span> • Đơn
                thuốc <span className="font-mono">{detail.rxCode}</span>
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
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          <DispensingReceiptCard detail={{ ...detail, dispenseCode: activeDispenseCode }} />
        </main>

        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
          <Button variant="secondary" size="sm" onClick={() => navigate('/pharmacy')}>
            Hoàn tất &amp; Quay lại Quầy
          </Button>

          <div className="flex flex-wrap items-center gap-2.5">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setNotice(`Đã xuất biên nhận PDF ${activeDispenseCode}.pdf`)}
              className="gap-1.5"
            >
              <FileDown className="h-4 w-4" aria-hidden="true" />
              <span>Tải biên nhận PDF</span>
            </Button>

            <Button
              variant="primary"
              size="sm"
              onClick={() => window.print()}
              className="gap-1.5 shadow-xs"
            >
              <Printer className="h-4 w-4" aria-hidden="true" />
              <span>In biên nhận bàn giao</span>
            </Button>
          </div>
        </footer>
      </div>
    </div>
  );
}
