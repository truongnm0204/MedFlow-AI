import { useEffect, useState } from 'react';
import { ChevronRight, Home } from 'lucide-react';
import { Link } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Card } from '@/shared/ui/card';
import { toast } from '@/shared/ui/toast';
import { useQueryClient } from '@tanstack/react-query';

import { ClaimStatusCard } from '../components/claim-status-card';
import { IdentityClaimForm } from '../components/identity-claim-form';
import {
  useCreateIdentityClaimMutation,
  useIdentityClaimQuery,
  useIdentityHospitalsQuery,
  useMyIdentityClaimsQuery,
  useSendIdentityClaimOtpMutation,
} from '../hooks/use-identity-claims';

export function PatientIdentityClaimPage() {
  const queryClient = useQueryClient();
  const claimsQuery = useMyIdentityClaimsQuery();
  const hospitalsQuery = useIdentityHospitalsQuery();
  const sendOtp = useSendIdentityClaimOtpMutation();
  const createClaim = useCreateIdentityClaimMutation();

  const [claimId, setClaimId] = useState<number | undefined>();
  const [formOpen, setFormOpen] = useState(false);
  const [otpCooldown, setOtpCooldown] = useState(0);

  const latest = claimsQuery.data?.[0];
  const activeId = claimId ?? latest?.claimId;
  const statusQuery = useIdentityClaimQuery(activeId);
  const claim = statusQuery.data ?? latest;
  const isOpen = claim?.status === 'PENDING' || claim?.status === 'NEEDS_RECONCILIATION';

  useEffect(() => {
    if (!otpCooldown) return;
    const timer = window.setTimeout(() => setOtpCooldown((value) => value - 1), 1000);
    return () => window.clearTimeout(timer);
  }, [otpCooldown]);

  useEffect(() => {
    if (claim?.status !== 'APPROVED') return;
    for (const key of ['patient-profile', 'my-visits', 'family-members']) {
      void queryClient.invalidateQueries({ queryKey: [key] });
    }
  }, [claim?.status, queryClient]);

  const handleSendOtp = async () => {
    try {
      await sendOtp.mutateAsync();
      setOtpCooldown(45);
      toast.success('Đã gửi OTP tới email tài khoản của bạn.');
    } catch {
      // lỗi hiển thị qua sendOtp.error
    }
  };

  const handleSubmit = async (payload: {
    patientCode: string;
    hospitalId: number;
    otp: string;
  }) => {
    try {
      const created = await createClaim.mutateAsync(payload);
      setClaimId(created.claimId);
      setFormOpen(false);
      toast.success('Đã gửi yêu cầu liên kết hồ sơ.');
    } catch {
      // lỗi hiển thị qua createClaim.error
    }
  };

  const showForm = formOpen || !claim;

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Liên kết hồ sơ</span>
          </nav>

          <h1 className="text-2xl font-extrabold text-ink sm:text-3xl">Liên kết hồ sơ bệnh nhân</h1>
          <p className="mt-1 text-sm text-sub">
            Nhận lại lịch sử khám và viện phí khi bạn đã từng khám tại bệnh viện trước khi tạo tài
            khoản.
          </p>

          <div className="mt-5 space-y-4">
            {sendOtp.isError ? <Alert tone="error">{getErrorMessage(sendOtp.error)}</Alert> : null}
            {createClaim.isError ? (
              <Alert tone="error">{getErrorMessage(createClaim.error)}</Alert>
            ) : null}

            {claim ? (
              <ClaimStatusCard
                claim={claim}
                onStartNew={() => {
                  setFormOpen(true);
                  setClaimId(undefined);
                }}
              />
            ) : null}

            {isOpen && claim ? (
              <Card className="p-4 text-xs text-sub" role="status">
                Trạng thái tự cập nhật mỗi 10 giây. Bạn có thể rời trang và quay lại sau.
              </Card>
            ) : null}

            {showForm ? (
              <IdentityClaimForm
                hospitals={hospitalsQuery.data ?? []}
                isSendingOtp={sendOtp.isPending}
                isSubmitting={createClaim.isPending}
                otpCooldown={otpCooldown}
                onSendOtp={() => void handleSendOtp()}
                onSubmit={(payload) => void handleSubmit(payload)}
              />
            ) : null}

            {claimsQuery.isError && !claim ? (
              <Alert tone="error">{getErrorMessage(claimsQuery.error)}</Alert>
            ) : null}
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
