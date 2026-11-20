import { useState, type FormEvent } from 'react';
import { KeyRound, Link2, Send } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Card } from '@/shared/ui/card';
import { Input } from '@/shared/ui/input';

import type { IdentityHospital } from '../types/identity-claim.types';

interface IdentityClaimFormProps {
  hospitals: IdentityHospital[];
  isSendingOtp: boolean;
  isSubmitting: boolean;
  otpCooldown: number;
  onSendOtp: () => void;
  onSubmit: (payload: { patientCode: string; hospitalId: number; otp: string }) => void;
}

export function IdentityClaimForm({
  hospitals,
  isSendingOtp,
  isSubmitting,
  otpCooldown,
  onSendOtp,
  onSubmit,
}: IdentityClaimFormProps) {
  const [patientCode, setPatientCode] = useState('');
  const [hospitalId, setHospitalId] = useState('');
  const [otp, setOtp] = useState('');

  const canSubmit =
    patientCode.trim().length > 0 && hospitalId !== '' && otp.trim().length === 6 && !isSubmitting;

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!canSubmit) return;
    onSubmit({ patientCode: patientCode.trim(), hospitalId: Number(hospitalId), otp: otp.trim() });
  };

  return (
    <Card className="p-6">
      <div className="flex items-start gap-3">
        <Link2 className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
        <div>
          <h2 className="text-sm font-bold text-ink">Liên kết hồ sơ đã có tại bệnh viện</h2>
          <p className="mt-1 text-xs leading-relaxed text-sub">
            Dùng mã hồ sơ trên phiếu khám hoặc sổ khám. Bạn cần xác thực OTP gửi tới email tài khoản
            trước khi gửi yêu cầu.
          </p>
        </div>
      </div>

      <form className="mt-5 space-y-4" onSubmit={handleSubmit}>
        <Input
          label="Mã hồ sơ bệnh nhân"
          placeholder="Ví dụ: BN-2026-0100"
          value={patientCode}
          maxLength={40}
          onChange={(event) => setPatientCode(event.target.value)}
          required
        />

        <label className="block">
          <span className="mb-1 block text-xs font-semibold text-ink">Cơ sở xác minh</span>
          <select
            value={hospitalId}
            onChange={(event) => setHospitalId(event.target.value)}
            required
            className="w-full rounded-lg border border-border bg-white px-3 py-2.5 text-sm text-ink focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">Chọn cơ sở sẽ đến xác minh</option>
            {hospitals.map((hospital) => (
              <option key={hospital.id} value={hospital.id} disabled={!hospital.isActive}>
                {hospital.name}
              </option>
            ))}
          </select>
        </label>

        <div className="rounded-xl border border-border bg-canvas p-4">
          <div className="flex flex-wrap items-end gap-3">
            <div className="min-w-40 flex-1">
              <Input
                label="Mã OTP"
                placeholder="6 chữ số"
                inputMode="numeric"
                maxLength={6}
                value={otp}
                onChange={(event) => setOtp(event.target.value.replace(/\D/g, ''))}
                leftIcon={<KeyRound className="h-4 w-4" aria-hidden="true" />}
                required
              />
            </div>
            <Button
              type="button"
              variant="secondary"
              size="md"
              loading={isSendingOtp}
              disabled={otpCooldown > 0}
              onClick={onSendOtp}
            >
              <Send className="h-4 w-4" aria-hidden="true" />
              <span>{otpCooldown > 0 ? `Gửi lại sau ${otpCooldown}s` : 'Gửi OTP'}</span>
            </Button>
          </div>
          <p className="mt-2 text-[11px] text-sub">
            OTP có hiệu lực 5 phút và chỉ dùng một lần cho yêu cầu này.
          </p>
        </div>

        <Button
          type="submit"
          variant="primary"
          size="lg"
          loading={isSubmitting}
          disabled={!canSubmit}
        >
          Gửi yêu cầu liên kết
        </Button>
      </form>
    </Card>
  );
}
