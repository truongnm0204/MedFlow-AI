import { Lock, Mail } from 'lucide-react'
import type { FieldErrors, UseFormRegister } from 'react-hook-form'

import { Input, PasswordInput } from '@/shared/ui'

import { PasswordStrength } from './password-strength'
import type { RegisterFormValues } from '../schemas/auth.schemas'

interface Props {
  register: UseFormRegister<RegisterFormValues>
  errors: FieldErrors<RegisterFormValues>
  password: string
}

/** Nhóm 1: tài khoản đăng nhập (email, mật khẩu). */
export function RegisterAccountSection({ register, errors, password }: Props) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="flex items-center gap-2 border-b border-slate-100 pb-2 text-xs font-bold uppercase tracking-wider text-ink">
        <Lock className="h-3.5 w-3.5 text-primary" />
        Nhóm 1: Thông tin tài khoản đăng nhập
      </h2>

      <Input
        label="Email đăng ký nhận mã xác thực"
        type="email"
        required
        autoComplete="email"
        placeholder="nguyenvana.patient@gmail.com"
        leftIcon={<Mail className="h-4 w-4" />}
        error={errors.email?.message}
        {...register('email')}
      />
      <p className="-mt-2 text-[11px] text-slate-400">
        Email dùng để đăng nhập và nhận thông báo kết quả khám ngoại trú bảo mật.
      </p>

      <div className="grid gap-4 sm:grid-cols-2">
        <PasswordInput
          label="Mật khẩu bảo mật"
          autoComplete="new-password"
          placeholder="Tối thiểu 8 ký tự"
          error={errors.password?.message}
          {...register('password')}
        />
        <PasswordInput
          label="Xác nhận mật khẩu"
          autoComplete="new-password"
          placeholder="Nhập lại mật khẩu"
          error={errors.confirmPassword?.message}
          {...register('confirmPassword')}
        />
      </div>

      <PasswordStrength password={password} />
    </section>
  )
}
