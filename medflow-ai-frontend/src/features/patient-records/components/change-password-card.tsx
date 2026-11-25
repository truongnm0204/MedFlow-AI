import { zodResolver } from '@hookform/resolvers/zod';
import { LockKeyhole, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';

import { PasswordStrength } from '@/features/auth/components/password-strength';
import { useChangePassword } from '@/features/auth/hooks/use-auth';
import {
  changePasswordSchema,
  type ChangePasswordFormValues,
} from '@/features/auth/schemas/auth.schemas';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button, PasswordInput } from '@/shared/ui';

export function ChangePasswordCard() {
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const changePassword = useChangePassword();

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: {
      currentPassword: '',
      newPassword: '',
      confirmPassword: '',
    },
  });

  const newPasswordValue = useWatch({ control, name: 'newPassword' }) ?? '';

  const onSubmit = (values: ChangePasswordFormValues) => {
    setSuccessMessage(null);
    changePassword.mutate(
      {
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      },
      {
        onSuccess: (res) => {
          reset({
            currentPassword: '',
            newPassword: '',
            confirmPassword: '',
          });
          setSuccessMessage(res.message);
        },
      },
    );
  };

  return (
    <div className="rounded-2xl border border-border bg-white p-6 shadow-xs">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-2.5">
          <LockKeyhole className="h-5 w-5 text-primary" aria-hidden="true" />
          <h2 className="text-base font-bold text-navy">Đổi mật khẩu đăng nhập</h2>
        </div>
        <span className="inline-flex items-center gap-1 rounded-full bg-sky-50 px-2.5 py-1 text-[11px] font-semibold text-primary">
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Giữ phiên thiết bị hiện tại</span>
        </span>
      </div>

      <p className="mt-3 text-xs leading-relaxed text-sub">
        Sau khi cập nhật mật khẩu, phiên làm việc trên trình duyệt này vẫn được duy trì; các phiên
        đăng nhập trên thiết bị hoặc trình duyệt khác sẽ tự động bị thu hồi để bảo vệ hồ sơ y tế.
      </p>

      {successMessage && (
        <Alert tone="success" className="mt-4">
          {successMessage}
        </Alert>
      )}

      {changePassword.isError && (
        <Alert tone="error" className="mt-4">
          {getErrorMessage(
            changePassword.error,
            'Không thể đổi mật khẩu lúc này. Vui lòng kiểm tra lại mật khẩu hiện tại.',
          )}
        </Alert>
      )}

      <form onSubmit={handleSubmit(onSubmit)} className="mt-4 space-y-4" noValidate>
        <PasswordInput
          label="Mật khẩu hiện tại"
          autoComplete="current-password"
          placeholder="Nhập mật khẩu đang sử dụng"
          error={errors.currentPassword?.message}
          {...register('currentPassword')}
        />

        <div>
          <PasswordInput
            label="Mật khẩu mới"
            autoComplete="new-password"
            placeholder="Tối thiểu 8 ký tự, gồm chữ hoa, chữ thường và số"
            error={errors.newPassword?.message}
            {...register('newPassword')}
          />
          <div className="mt-2">
            <PasswordStrength password={newPasswordValue} />
          </div>
        </div>

        <PasswordInput
          label="Xác nhận mật khẩu mới"
          autoComplete="new-password"
          placeholder="Nhập lại mật khẩu mới"
          error={errors.confirmPassword?.message}
          {...register('confirmPassword')}
        />

        <div className="flex justify-end border-t border-border pt-4">
          <Button type="submit" loading={changePassword.isPending}>
            Cập nhật mật khẩu
          </Button>
        </div>
      </form>
    </div>
  );
}
