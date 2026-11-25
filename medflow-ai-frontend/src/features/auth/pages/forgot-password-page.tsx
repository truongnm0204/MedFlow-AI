import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeft, Clock, KeyRound, Mail, RefreshCw, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link } from 'react-router-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button, Input, PasswordInput } from '@/shared/ui';

import { AuthLayout } from '../components/auth-layout';
import { OtpInput } from '../components/otp-input';
import { PasswordStrength } from '../components/password-strength';
import { useForgotPassword, useResetPassword } from '../hooks/use-auth';
import { useCountdown } from '../hooks/use-countdown';
import {
  forgotPasswordSchema,
  resetPasswordSchema,
  type ForgotPasswordFormValues,
  type ResetPasswordFormValues,
} from '../schemas/auth.schemas';

const OTP_TTL_SECONDS = 300;
const RESEND_COOLDOWN_SECONDS = 45;

function maskEmail(email: string): string {
  const [local, domain] = email.split('@');
  if (!domain) return email;
  const visible = local.slice(0, Math.min(3, local.length));
  return `${visible}***@${domain}`;
}

function formatCountdown(totalSeconds: number): string {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export function ForgotPasswordPage() {
  const [step, setStep] = useState<'request' | 'reset'>('request');
  const [targetEmail, setTargetEmail] = useState('');
  const [infoMessage, setInfoMessage] = useState<string | null>(null);

  const forgotPassword = useForgotPassword();
  const resetPassword = useResetPassword();
  const ttl = useCountdown(OTP_TTL_SECONDS);
  const cooldown = useCountdown(RESEND_COOLDOWN_SECONDS);

  const requestForm = useForm<ForgotPasswordFormValues>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: '' },
  });

  const resetForm = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { otpCode: '', newPassword: '', confirmPassword: '' },
  });

  const otpValue = useWatch({ control: resetForm.control, name: 'otpCode' }) ?? '';
  const newPasswordValue = useWatch({ control: resetForm.control, name: 'newPassword' }) ?? '';

  const handleRequestOtp = (values: ForgotPasswordFormValues) => {
    const normalizedEmail = values.email.trim();
    forgotPassword.mutate(
      { email: normalizedEmail },
      {
        onSuccess: (res) => {
          setTargetEmail(normalizedEmail);
          setInfoMessage(res.message);
          resetForm.reset({ otpCode: '', newPassword: '', confirmPassword: '' });
          ttl.restart(OTP_TTL_SECONDS);
          cooldown.restart(RESEND_COOLDOWN_SECONDS);
          setStep('reset');
        },
      },
    );
  };

  const handleResendOtp = () => {
    if (!targetEmail || cooldown.isRunning || forgotPassword.isPending) return;
    forgotPassword.mutate(
      { email: targetEmail },
      {
        onSuccess: (res) => {
          setInfoMessage(res.message);
          resetForm.setValue('otpCode', '', { shouldValidate: false });
          ttl.restart(OTP_TTL_SECONDS);
          cooldown.restart(RESEND_COOLDOWN_SECONDS);
        },
      },
    );
  };

  const handleResetPassword = (values: ResetPasswordFormValues) => {
    resetPassword.mutate({
      email: targetEmail,
      otpCode: values.otpCode,
      newPassword: values.newPassword,
    });
  };

  return (
    <AuthLayout>
      <div className="w-full max-w-md">
        <div className="mb-6 flex items-center justify-between">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-primary">
            <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
            Khôi phục mật khẩu
          </span>
          <Link
            to="/login"
            className="inline-flex items-center gap-1 text-xs font-medium text-sub hover:text-ink"
          >
            <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
            Đăng nhập
          </Link>
        </div>

        <div className="rounded-2xl border border-border bg-white p-8 shadow-xl shadow-slate-200/60 sm:p-10">
          {step === 'request' ? (
            <>
              <h1 className="text-2xl font-bold tracking-tight text-ink">Quên mật khẩu?</h1>
              <p className="mt-1.5 text-sm leading-relaxed text-sub">
                Nhập địa chỉ email tài khoản MedFlow AI để nhận mã OTP khôi phục gồm 6 chữ số.
              </p>

              {forgotPassword.isError && (
                <Alert tone="error" className="mt-5">
                  {getErrorMessage(
                    forgotPassword.error,
                    'Không thể gửi yêu cầu khôi phục lúc này. Vui lòng thử lại sau.',
                  )}
                </Alert>
              )}

              <form
                onSubmit={requestForm.handleSubmit(handleRequestOtp)}
                className="mt-6 flex flex-col gap-5"
                noValidate
              >
                <Input
                  label="Email đăng ký"
                  type="email"
                  autoComplete="email"
                  placeholder="nguyenvana@gmail.com"
                  leftIcon={<Mail className="h-4 w-4" aria-hidden="true" />}
                  error={requestForm.formState.errors.email?.message}
                  {...requestForm.register('email')}
                />

                <Button
                  type="submit"
                  size="lg"
                  loading={forgotPassword.isPending}
                  className="w-full"
                >
                  Gửi mã OTP khôi phục
                </Button>
              </form>

              <div className="mt-6 flex items-start gap-2.5 rounded-xl border border-border bg-canvas p-3.5 text-xs leading-relaxed text-sub">
                <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                <p>
                  Vì lý do bảo mật, hệ thống chỉ gửi mã OTP tới tài khoản đăng nhập bằng mật khẩu
                  nội bộ đang hoạt động và có hiệu lực trong 5 phút.
                </p>
              </div>
            </>
          ) : (
            <>
              <h1 className="text-2xl font-bold tracking-tight text-ink">Đặt lại mật khẩu mới</h1>
              <p className="mt-1.5 text-sm leading-relaxed text-sub">
                Nhập mã OTP 6 chữ số gửi tới{' '}
                <span className="font-mono font-semibold text-ink">{maskEmail(targetEmail)}</span>{' '}
                và thiết lập mật khẩu mới.
              </p>

              <div className="mt-3 flex items-center justify-between text-xs">
                <span className=" inline-flex items-center gap-1.5 text-sub">
                  <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                  Hiệu lực mã:{' '}
                  <strong className="font-mono text-navy">{formatCountdown(ttl.remaining)}</strong>
                </span>
                <button
                  type="button"
                  onClick={() => {
                    resetPassword.reset();
                    setStep('request');
                  }}
                  className="font-semibold text-primary hover:text-primary-dark"
                >
                  Đổi email khác
                </button>
              </div>

              {infoMessage && (
                <Alert tone="info" className="mt-4">
                  {infoMessage}
                </Alert>
              )}

              {forgotPassword.isError && (
                <Alert tone="error" className="mt-4">
                  {getErrorMessage(forgotPassword.error, 'Không thể gửi lại mã OTP lúc này.')}
                </Alert>
              )}

              {resetPassword.isError && (
                <Alert tone="error" className="mt-4">
                  {getErrorMessage(
                    resetPassword.error,
                    'Mã OTP không hợp lệ hoặc đã hết hạn. Vui lòng kiểm tra lại.',
                  )}
                </Alert>
              )}

              <form
                onSubmit={resetForm.handleSubmit(handleResetPassword)}
                className="mt-5 flex flex-col gap-4"
                noValidate
              >
                <div>
                  <label className="mb-2 block text-xs font-semibold uppercase tracking-wider text-navy">
                    Mã xác thực OTP (6 chữ số)
                  </label>
                  <OtpInput
                    value={otpValue}
                    onChange={(val) =>
                      resetForm.setValue('otpCode', val, { shouldValidate: val.length === 6 })
                    }
                    disabled={resetPassword.isPending}
                  />
                  {resetForm.formState.errors.otpCode && (
                    <p role="alert" className="mt-1.5 text-xs font-medium text-danger">
                      {resetForm.formState.errors.otpCode.message}
                    </p>
                  )}
                </div>

                <div>
                  <PasswordInput
                    label="Mật khẩu mới"
                    autoComplete="new-password"
                    placeholder="Tối thiểu 8 ký tự, chữ hoa, chữ thường & số"
                    error={resetForm.formState.errors.newPassword?.message}
                    {...resetForm.register('newPassword')}
                  />
                  <div className="mt-2">
                    <PasswordStrength password={newPasswordValue} />
                  </div>
                </div>

                <PasswordInput
                  label="Xác nhận mật khẩu mới"
                  autoComplete="new-password"
                  placeholder="Nhập lại mật khẩu mới"
                  error={resetForm.formState.errors.confirmPassword?.message}
                  {...resetForm.register('confirmPassword')}
                />

                <Button
                  type="submit"
                  size="lg"
                  loading={resetPassword.isPending}
                  className="mt-1 w-full"
                >
                  Xác nhận đặt lại mật khẩu
                </Button>
              </form>

              <div className="mt-5 border-t border-border pt-4 text-center text-xs text-sub">
                Chưa nhận được mã?{' '}
                <button
                  type="button"
                  onClick={handleResendOtp}
                  disabled={cooldown.isRunning || forgotPassword.isPending}
                  className="inline-flex items-center gap-1 font-semibold text-primary hover:text-primary-dark disabled:cursor-not-allowed disabled:text-slate-400"
                >
                  <RefreshCw
                    className={`h-3 w-3 ${forgotPassword.isPending ? 'animate-spin' : ''}`}
                    aria-hidden="true"
                  />
                  {cooldown.isRunning
                    ? `Gửi lại mã OTP (sau ${cooldown.remaining}s)`
                    : 'Gửi lại mã OTP'}
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </AuthLayout>
  );
}
