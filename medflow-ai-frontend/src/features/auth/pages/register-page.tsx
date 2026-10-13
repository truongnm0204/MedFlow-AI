import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeft, ChevronRight, Home } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button } from '@/shared/ui';
import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

import { FormField } from '../components/form-field';
import { RegisterAccountSection } from '../components/register-account-section';
import { RegisterIdentitySection } from '../components/register-identity-section';
import { useRegister } from '../hooks/use-auth';
import { registerSchema, type RegisterFormValues } from '../schemas/auth.schemas';

export function RegisterPage() {
  const registerMutation = useRegister();

  const {
    register,
    control,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { gender: 'MALE' },
  });

  const password = watch('password') ?? '';

  const onSubmit = (values: RegisterFormValues) => {
    const { acceptTerms: _acceptTerms, ...payload } = values;
    registerMutation.mutate(payload);
  };

  return (
    <div className="min-h-screen bg-canvas">
      {/* Header đơn giản theo Figma `1:345` */}
      <header className="sticky top-0 z-40 border-b border-border bg-white/90 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-6">
          <MedflowLogo variant="full" className="h-10 w-auto" />
          <div className="flex items-center gap-2 text-sm">
            <span className="hidden text-slate-500 sm:inline">Đã có tài khoản?</span>
            <Link
              to="/login"
              className="rounded-lg border border-sky-200/60 bg-sky-50 px-4 py-2 text-xs font-semibold text-primary hover:bg-sky-100"
            >
              Đăng nhập ngay
            </Link>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
        {/* Breadcrumb */}
        <nav
          className="mb-6 flex items-center gap-1.5 text-xs text-slate-500"
          aria-label="Breadcrumb"
        >
          <Link to="/" className="flex items-center gap-1 hover:text-slate-700">
            <Home className="h-3 w-3" />
            Trang chủ
          </Link>
          <ChevronRight className="h-3 w-3 text-slate-300" />
          <span className="font-medium text-ink">Đăng ký Hồ sơ Khám Ngoại trú</span>
        </nav>

        <div className="mx-auto w-full max-w-2xl rounded-xl border border-border bg-white p-6 shadow-sm sm:p-10">
          <div className="mb-8">
            <h1 className="text-2xl font-bold tracking-tight text-ink">
              Đăng ký Tài khoản Bệnh nhân
            </h1>
            <p className="mt-1.5 text-sm text-slate-500">
              Tạo tài khoản để đặt lịch khám và quản lý hồ sơ khám bệnh ngoại trú
            </p>
          </div>

          {registerMutation.isError && (
            <Alert tone="error" className="mb-6">
              {getErrorMessage(registerMutation.error, 'Đăng ký thất bại.')}
            </Alert>
          )}

          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-6" noValidate>
            <RegisterAccountSection register={register} errors={errors} password={password} />
            <RegisterIdentitySection register={register} control={control} errors={errors} />

            {/* Điều khoản */}
            <FormField label="" error={errors.acceptTerms?.message}>
              <label className="flex cursor-pointer items-start gap-3 text-xs leading-relaxed text-slate-600">
                <input
                  type="checkbox"
                  className="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary/40"
                  {...register('acceptTerms')}
                />
                <span>
                  Tôi đồng ý với{' '}
                  <a href="#" className="font-medium text-primary underline">
                    Điều khoản dịch vụ
                  </a>{' '}
                  và{' '}
                  <a href="#" className="font-medium text-primary underline">
                    Chính sách bảo mật dữ liệu y tế
                  </a>{' '}
                  của MedFlow.
                </span>
              </label>
            </FormField>

            {/* Actions */}
            <div className="flex items-center justify-between border-t border-slate-100 pt-4">
              <Link
                to="/login"
                className="flex items-center gap-1.5 px-4 py-2.5 text-xs font-semibold text-slate-600 hover:text-slate-800"
              >
                <ArrowLeft className="h-3 w-3" />
                Hủy / Quay lại Đăng nhập
              </Link>
              <Button type="submit" loading={registerMutation.isPending}>
                Tạo tài khoản &amp; Tiếp tục →
              </Button>
            </div>
          </form>
        </div>
      </main>
    </div>
  );
}
