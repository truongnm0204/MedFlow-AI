import { Calendar, IdCard, Info, Phone, User } from 'lucide-react'
import type { Control, FieldErrors, UseFormRegister } from 'react-hook-form'
import { Controller } from 'react-hook-form'

import { Input } from '@/shared/ui'

import { FormField } from './form-field'
import type { RegisterFormValues } from '../schemas/auth.schemas'

const GENDERS = [
  { value: 'MALE', label: 'Nam' },
  { value: 'FEMALE', label: 'Nữ' },
  { value: 'OTHER', label: 'Khác' },
] as const

interface Props {
  register: UseFormRegister<RegisterFormValues>
  control: Control<RegisterFormValues>
  errors: FieldErrors<RegisterFormValues>
}

/** Nhóm 2: định danh cá nhân (tên, ngày sinh, giới tính, SĐT). */
export function RegisterIdentitySection({ register, control, errors }: Props) {
  return (
    <section className="flex flex-col gap-4 pt-2">
      <div className="flex items-center justify-between border-b border-slate-100 pb-2">
        <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-ink">
          <IdCard className="h-3.5 w-3.5 text-primary" />
          Nhóm 2: Thông tin định danh cá nhân
        </h2>
        <span className="hidden text-[11px] text-slate-500 sm:inline">
          Chuẩn khám ngoại trú tự nguyện
        </span>
      </div>

      <Input
        label="Họ và tên người bệnh (Theo CCCD/Định danh)"
        required
        autoComplete="name"
        placeholder="NGUYỄN VĂN AN"
        leftIcon={<User className="h-4 w-4" />}
        className="uppercase"
        error={errors.fullName?.message}
        {...register('fullName')}
      />

      <div className="grid grid-cols-12 gap-4">
        <div className="col-span-12 sm:col-span-7">
          <Input
            label="Ngày tháng năm sinh"
            type="date"
            required
            autoComplete="bday"
            leftIcon={<Calendar className="h-4 w-4" />}
            error={errors.dateOfBirth?.message}
            {...register('dateOfBirth')}
          />
        </div>

        <Controller
          name="gender"
          control={control}
          render={({ field }) => (
            <FormField label="Giới tính" required error={errors.gender?.message} className="col-span-12 sm:col-span-5">
              <div className="flex gap-1.5 rounded-lg border border-slate-200 bg-slate-100 p-1">
                {GENDERS.map(({ value, label }) => (
                  <button
                    key={value}
                    type="button"
                    onClick={() => field.onChange(value)}
                    className={`flex-1 rounded py-1.5 text-xs font-semibold transition-colors ${
                      field.value === value
                        ? 'bg-white text-primary shadow-sm'
                        : 'text-slate-600 hover:text-slate-800'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </FormField>
          )}
        />
      </div>

      <Input
        label="Số điện thoại liên hệ"
        type="tel"
        required
        autoComplete="tel"
        placeholder="0912 345 678"
        leftIcon={<Phone className="h-4 w-4" />}
        error={errors.phone?.message}
        {...register('phone')}
      />

      <div className="flex gap-2.5 rounded-lg border border-sky-100 bg-sky-50/70 p-3.5 text-xs leading-relaxed">
        <Info className="h-4 w-4 shrink-0 text-sky-600" />
        <p className="text-slate-600">
          <span className="font-semibold text-sky-900">Chế độ Khám Ngoại trú Tự nguyện:</span>{' '}
          Mã hồ sơ bệnh án điện tử cá nhân (PID) sẽ được khởi tạo ngay khi tài khoản được kích hoạt.
        </p>
      </div>
    </section>
  )
}
