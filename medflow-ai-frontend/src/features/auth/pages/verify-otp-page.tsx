import { ArrowLeft, CheckCircle2, Clock, Info, MailCheck, RefreshCw, ShieldCheck } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, Navigate, useSearchParams } from 'react-router-dom'

import { getErrorMessage } from '@/shared/lib/get-error-message'
import { Alert, Button } from '@/shared/ui'
import { MedflowLogo } from '@/shared/components/layout/medflow-logo'

import { OtpInput } from '../components/otp-input'
import { useCountdown } from '../hooks/use-countdown'
import { useResendOtp, useVerifyOtp } from '../hooks/use-auth'

const OTP_TTL_SECONDS = 300 // 5 phút — khớp backend
const RESEND_COOLDOWN_SECONDS = 45

/** Mask email: `nguyenvana@gmail.com` → `ngu***@gmail.com`. */
function maskEmail(email: string): string {
  const [local, domain] = email.split('@')
  if (!domain) return email
  const visible = local.slice(0, Math.min(3, local.length))
  return `${visible}***@${domain}`
}

function formatCountdown(totalSeconds: number): string {
  const m = Math.floor(totalSeconds / 60)
  const s = totalSeconds % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

export function VerifyOtpPage() {
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''

  const [otp, setOtp] = useState('')
  const verifyOtp = useVerifyOtp()
  const resendOtp = useResendOtp()
  const ttl = useCountdown(OTP_TTL_SECONDS)
  const cooldown = useCountdown(RESEND_COOLDOWN_SECONDS)

  // Cooldown resend bắt đầu ngay khi vào trang (vừa gửi OTP lúc register).
  useEffect(() => {
    cooldown.restart(RESEND_COOLDOWN_SECONDS)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const masked = useMemo(() => maskEmail(email), [email])

  if (!email) return <Navigate to="/register" replace />

  const canSubmit = otp.length === 6 && !verifyOtp.isPending

  const handleResend = () => {
    resendOtp.mutate(email, {
      onSuccess: () => {
        setOtp('')
        ttl.restart(OTP_TTL_SECONDS)
        cooldown.restart(RESEND_COOLDOWN_SECONDS)
      },
    })
  }

  return (
    <div className="relative flex min-h-screen flex-col bg-canvas">
      {/* Header đơn giản */}
      <header className="sticky top-0 z-40 border-b border-border bg-white/90 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-6">
          <MedflowLogo variant="full" className="h-10 w-auto" />
          <span className="rounded-full border border-slate-200 bg-slate-100/80 px-3.5 py-1.5 text-xs text-slate-600">
            Tiếp đón &amp; CSKH 24/7: <strong className="text-ink">1900 8899</strong>
          </span>
        </div>
      </header>

      {/* Ambient geometry */}
      <div className="pointer-events-none absolute left-1/4 top-40 h-96 w-96 rounded-full bg-sky-100/40 blur-3xl" />
      <div className="pointer-events-none absolute bottom-20 right-1/4 h-72 w-72 rounded-full bg-slate-200/40 blur-3xl" />

      <main className="relative flex flex-1 items-center justify-center px-4 py-12">
        <div className="w-full max-w-lg rounded-2xl border border-border bg-white px-8 py-10 shadow-xl shadow-slate-200/50 sm:px-10">
          {/* Icon */}
          <div className="mb-4 flex justify-center">
            <div className="relative">
              <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-sky-500 to-sky-700 shadow-lg shadow-sky-600/30">
                <MailCheck className="h-7 w-7 text-white" />
              </div>
              <span className="absolute -bottom-1 -right-1 flex h-6 w-6 items-center justify-center rounded-full border-2 border-white bg-ink">
                <ShieldCheck className="h-3 w-3 text-white" />
              </span>
            </div>
          </div>

          <h1 className="text-center text-[26px] font-bold tracking-tight text-ink">
            Xác thực Tài khoản Người bệnh
          </h1>
          <p className="mt-2 text-center text-sm leading-relaxed text-slate-600">
            Mã xác thực kích hoạt 6 chữ số (OTP) đã được gửi tới địa chỉ email đăng ký hồ sơ của bạn.
          </p>

          {/* Masked email + trạng thái */}
          <div className="mt-5 flex items-center justify-between rounded-xl border border-slate-200/90 bg-canvas px-4 py-3.5">
            <div className="flex items-center gap-2.5">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-100">
                <MailCheck className="h-4 w-4 text-sky-700" />
              </span>
              <div>
                <p className="text-[11px] font-medium text-slate-500">Hộp thư nhận mã kích hoạt</p>
                <p className="font-mono text-sm font-bold tracking-wide text-slate-800">{masked}</p>
              </div>
            </div>
            <span className="flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-700">
              <span className="h-2 w-2 rounded-full bg-amber-500" />
              Chờ kích hoạt
            </span>
          </div>

          {/* OTP form */}
          <div className="mt-6">
            <p className="mb-3 text-xs font-bold uppercase tracking-wider text-slate-700">
              Nhập mã 6 chữ số kích hoạt
            </p>
            <OtpInput value={otp} onChange={setOtp} disabled={verifyOtp.isPending} />

            <div className="mt-4 flex items-center justify-center gap-2 text-xs text-slate-600">
              <Clock className="h-3.5 w-3.5" />
              Mã có hiệu lực trong:
              <span className="rounded border border-sky-100 bg-sky-50 px-2 py-0.5 font-mono text-sm font-bold text-sky-800">
                {formatCountdown(ttl.remaining)}
              </span>
            </div>

            {verifyOtp.isError && (
              <Alert tone="error" className="mt-4">
                {getErrorMessage(verifyOtp.error, 'Mã xác thực không đúng hoặc đã hết hạn.')}
              </Alert>
            )}
            {resendOtp.isError && (
              <Alert tone="error" className="mt-4">
                {getErrorMessage(resendOtp.error, 'Không gửi lại được mã.')}
              </Alert>
            )}
            {resendOtp.isSuccess && (
              <Alert tone="success" className="mt-4">
                Đã gửi lại mã xác thực mới. Vui lòng kiểm tra hộp thư.
              </Alert>
            )}

            <Button
              size="lg"
              className="mt-5 w-full"
              disabled={!canSubmit}
              loading={verifyOtp.isPending}
              onClick={() => verifyOtp.mutate({ email, otp })}
            >
              <CheckCircle2 className="h-4.5 w-4.5" />
              Xác nhận kích hoạt tài khoản
            </Button>
          </div>

          {/* Resend + helper */}
          <div className="mt-7 flex flex-col gap-4 border-t border-slate-100 pt-6">
            <p className="text-center text-xs text-slate-600">
              Chưa nhận được mã?{' '}
              <button
                type="button"
                onClick={handleResend}
                disabled={cooldown.isRunning || resendOtp.isPending}
                className="inline-flex items-center gap-1 font-semibold text-primary hover:text-primary-dark disabled:cursor-not-allowed disabled:text-slate-400"
              >
                <RefreshCw className={`h-3 w-3 ${resendOtp.isPending ? 'animate-spin' : ''}`} />
                {cooldown.isRunning
                  ? `Gửi lại mã OTP (sau ${cooldown.remaining}s)`
                  : 'Gửi lại mã OTP'}
              </button>
            </p>

            <div className="flex gap-2.5 rounded-lg border border-slate-200/60 bg-canvas p-3 text-xs leading-relaxed">
              <Info className="h-4 w-4 shrink-0 text-slate-500" />
              <p className="text-slate-600">
                <span className="font-semibold text-slate-800">Mẹo tiếp nhận:</span> Vui lòng kiểm tra
                kỹ cả hộp thư <strong className="text-slate-700">Rác (Spam/Junk)</strong> nếu chưa
                thấy email sau 1 phút hoặc kiểm tra đúng hòm thư đã đăng ký.
              </p>
            </div>

            <Link
              to="/login"
              className="mx-auto flex items-center gap-1.5 text-xs font-medium text-slate-600 hover:text-slate-800"
            >
              <ArrowLeft className="h-3 w-3" />
              Quay lại Đăng nhập
            </Link>
          </div>
        </div>
      </main>

      <footer className="relative border-t border-slate-200/80 bg-slate-100/70 px-6 py-3">
        <p className="mx-auto flex max-w-6xl items-center justify-center gap-2 text-center text-xs text-slate-600">
          <ShieldCheck className="h-3.5 w-3.5" />
          Mã hóa đầu cuối 256-bit chuẩn HL7/FHIR bảo vệ dữ liệu sức khỏe cá nhân. Tuyệt đối không chia
          sẻ mã xác thực cho bất kỳ ai.
        </p>
      </footer>
    </div>
  )
}
