import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  PackageCheck,
  Printer,
  RefreshCw,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { DispenseIdentityVerificationCard } from '../components/dispense-identity-verification-card';
import { DispenseMedicationCounselingList } from '../components/dispense-medication-counseling-list';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  useConfirmMedicineDispense,
  useDispenseConfirmContext,
} from '../hooks/use-pharmacy-workbench';
import type { PharmacistHandoverCheck } from '../types/dispense-confirm.types';

export function DispenseConfirmPage() {
  const params = useParams<{ prescriptionCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeRxCode = params.prescriptionCode?.trim() || searchParams.get('rxCode')?.trim() || '';

  const contextQuery = useDispenseConfirmContext(activeRxCode);
  const confirmMutation = useConfirmMedicineDispense(activeRxCode);

  const [isIdentityVerified, setIsIdentityVerified] = useState(false);
  const [checks, setChecks] = useState<PharmacistHandoverCheck[]>([]);
  const [notice, setNotice] = useState<{ tone: 'error' | 'success'; message: string } | null>(null);
  const [dispensedCode, setDispensedCode] = useState<string | null>(null);

  useEffect(() => {
    if (contextQuery.data) {
      setIsIdentityVerified(contextQuery.data.receiver.isIdentityVerified);
      setChecks(contextQuery.data.checks);
      if (contextQuery.data.dispenseStatus === 'DISPENSED') {
        setDispensedCode(contextQuery.data.rxCode);
      }
    }
  }, [contextQuery.data]);

  const handleToggleCheck = (checkId: string) => {
    setChecks((prev) => prev.map((c) => (c.id === checkId ? { ...c, checked: !c.checked } : c)));
  };

  const handleConfirmDispense = async () => {
    const ctx = contextQuery.data;
    if (!ctx) return;

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

    try {
      const result = await confirmMutation.mutateAsync({
        prescriptionIdOrCode: ctx.rxCode,
        receiverName: ctx.receiver.receiverName,
        relationship: ctx.receiver.relationship,
        receiverIdentityCard: ctx.receiver.identityCard,
        receiverPhone: ctx.receiver.phone,
        isIdentityVerified: true,
        checksConfirmed: true,
        items: ctx.medications
          .filter((m) => m.prescriptionItemId != null)
          .map((m) => ({
            prescriptionItemId: m.prescriptionItemId as number,
            medicineBatchId: m.batchId ?? null,
            dispensedQuantity: m.dispensedQuantity ?? null,
          })),
      });

      setDispensedCode(result.dispenseCode);
      setNotice({
        tone: 'success',
        message: `Đã hoàn tất bàn giao đơn thuốc ${ctx.rxCode} cho bệnh nhân ${ctx.receiver.fullName}, trừ tồn kho FEFO và phát hành biên nhận ${result.dispenseCode}.`,
      });
    } catch (error) {
      setNotice({
        tone: 'error',
        message:
          error instanceof Error ? error.message : 'Không thể xác nhận bàn giao thuốc lúc này.',
      });
    }
  };

  const ctx = contextQuery.data;
  const isDispensed = Boolean(dispensedCode) || ctx?.dispenseStatus === 'DISPENSED';

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
            <div className="rounded-2xl border border-border bg-white p-8 text-center space-y-3">
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
              disabled={isDispensed || confirmMutation.isPending || ctx.canDispense === false}
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
