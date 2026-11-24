import { ArrowLeft, FileDown, PackageCheck, Printer, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { DispensingReceiptCard } from '../components/dispensing-receipt-card';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import { useDispensingResult } from '../hooks/use-dispensing-result';

export function DispensingResultPage() {
  const params = useParams<{ dispenseCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeDispenseCode = params.dispenseCode?.trim() || searchParams.get('code')?.trim() || '';
  const resultQuery = useDispensingResult(activeDispenseCode);
  const detail = resultQuery.data;
  const [notice, setNotice] = useState<string | null>(null);

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
              aria-label="Quay lại hàng đợi nhà thuốc"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="flex items-center gap-2 truncate text-sm font-bold text-navy">
                <PackageCheck className="h-4 w-4 text-emerald-600" aria-hidden="true" />
                <span>Kết quả Bàn giao Thuốc &amp; Biên nhận Cấp phát (SCR-PHA-04)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                {detail ? (
                  <>
                    Mã bàn giao: <span className="font-mono font-bold">{detail.dispenseCode}</span>{' '}
                    • Đơn thuốc <span className="font-mono">{detail.rxCode}</span>
                  </>
                ) : activeDispenseCode ? (
                  `Mã bàn giao: ${activeDispenseCode}`
                ) : (
                  'Chưa chọn biên nhận bàn giao'
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

        <main className="flex-1 space-y-5 overflow-y-auto p-6 pb-24">
          {!activeDispenseCode && (
            <div className="rounded-2xl border border-border bg-white p-8 text-center space-y-3">
              <p className="text-sm font-bold text-navy">
                Chưa chọn biên nhận cấp phát thuốc ngoại trú
              </p>
              <p className="text-xs text-sub">
                Vui lòng mở biên nhận từ lịch sử bàn giao hoặc từ Hàng đợi Nhà thuốc.
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

          {activeDispenseCode && resultQuery.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-8 text-center text-xs text-sub"
            >
              Đang tải biên nhận cấp phát thuốc...
            </div>
          )}

          {activeDispenseCode && !resultQuery.isPending && resultQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {resultQuery.error instanceof Error
                      ? resultQuery.error.message
                      : 'Không tìm thấy biên nhận bàn giao thuốc.'}
                  </span>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void resultQuery.refetch()}
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
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          {detail && <DispensingReceiptCard detail={detail} />}
        </main>

        {detail && (
          <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
            <Button variant="secondary" size="sm" onClick={() => navigate('/pharmacy')}>
              Hoàn tất &amp; Quay lại Quầy
            </Button>

            <div className="flex flex-wrap items-center gap-2.5">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setNotice(`Đã xuất biên nhận PDF ${detail.dispenseCode}.pdf`)}
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
        )}
      </div>
    </div>
  );
}
