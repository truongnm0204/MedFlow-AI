import { zodResolver } from '@hookform/resolvers/zod'
import { LifeBuoy, Mail } from 'lucide-react'
import { useForm } from 'react-hook-form'
import { Link, useSearchParams } from 'react-router-dom'

import { getErrorMessage } from '@/shared/lib/get-error-message'
import { Alert, Button, Divider, Input, PasswordInput } from '@/shared/ui'

import { AuthLayout } from '../components/auth-layout'
import { useLogin } from '../hooks/use-auth'
import { loginSchema, type LoginFormValues } from '../schemas/auth.schemas'

export function LoginPage() {
  const [params] = useSearchParams()
  const verified = params.get('verified') === '1'
  const login = useLogin()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) })

  const onSubmit = (values: LoginFormValues) => login.mutate(values)

  return (
    <AuthLayout>
      <div className="w-full max-w-md">
        <div className="mb-6 flex items-center justify-between">
          <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-primary">
            Cổng Đăng Nhập Duy Nhất
          </span>
          <a
            href="#"
            className="flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-slate-700"
          >
            <LifeBuoy className="h-3.5 w-3.5" />
            Trợ giúp
          </a>
        </div>

        <div className="rounded-2xl bg-white p-8 shadow-xl shadow-slate-200/60 sm:p-10">
          <h1 className="text-2xl font-bold tracking-tight text-ink">Đăng nhập</h1>
          <p className="mt-1 text-sm text-slate-500">
            Truy cập hồ sơ khám ngoại trú của bạn
          </p>

          {verified && (
            <Alert tone="success" className="mt-5">
              Tài khoản đã được kích hoạt. Vui lòng đăng nhập.
            </Alert>
          )}
          {login.isError && (
            <Alert tone="error" className="mt-5">
              {getErrorMessage(login.error, 'Đăng nhập thất bại.')}
            </Alert>
          )}

          <form onSubmit={handleSubmit(onSubmit)} className="mt-6 flex flex-col gap-5" noValidate>
            <Input
              label="Email hoặc tên đăng nhập"
              type="text"
              autoComplete="username"
              placeholder="nguyenvana@gmail.com"
              leftIcon={<Mail className="h-4 w-4" />}
              error={errors.identifier?.message}
              {...register('identifier')}
            />

            <PasswordInput
              label="Mật khẩu"
              autoComplete="current-password"
              placeholder="••••••••"
              error={errors.password?.message}
              {...register('password')}
            />

            <div className="flex items-center justify-between text-sm">
              <label className="flex cursor-pointer items-center gap-2 text-slate-600">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary/40"
                />
                Ghi nhớ đăng nhập
              </label>
              <a href="#" className="font-medium text-primary hover:text-primary-dark">
                Quên mật khẩu?
              </a>
            </div>

            <Button type="submit" size="lg" loading={login.isPending} className="w-full">
              Đăng nhập →
            </Button>
          </form>

          <Divider label="hoặc" className="my-6" />

          <p className="text-center text-sm text-slate-600">
            Chưa có tài khoản Bệnh nhân?{' '}
            <Link to="/register" className="font-semibold text-primary hover:text-primary-dark">
              Đăng ký khám ngay
            </Link>
          </p>
        </div>

        <p className="mt-6 text-center text-[11px] leading-relaxed text-slate-400">
          Bác sĩ, điều dưỡng và nhân viên y tế đăng nhập cùng cổng này bằng tài khoản được cấp.
        </p>
      </div>
    </AuthLayout>
  )
}
