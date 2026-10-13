import { z } from 'zod';

export const loginSchema = z.object({
  identifier: z.string().min(1, 'Vui lòng nhập email hoặc tên đăng nhập'),
  password: z.string().min(1, 'Vui lòng nhập mật khẩu'),
});
export type LoginFormValues = z.infer<typeof loginSchema>;

const PASSWORD_REGEX = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$/;

export const registerSchema = z
  .object({
    email: z.email('Email không hợp lệ'),
    password: z
      .string()
      .min(8, 'Mật khẩu tối thiểu 8 ký tự')
      .regex(PASSWORD_REGEX, 'Mật khẩu cần có chữ hoa, chữ thường và số'),
    confirmPassword: z.string().min(1, 'Vui lòng xác nhận mật khẩu'),
    fullName: z.string().min(2, 'Họ tên tối thiểu 2 ký tự'),
    dateOfBirth: z
      .string()
      .min(1, 'Vui lòng chọn ngày sinh')
      .refine((v) => !Number.isNaN(Date.parse(v)) && new Date(v) < new Date(), {
        message: 'Ngày sinh không hợp lệ',
      }),
    gender: z.enum(['MALE', 'FEMALE', 'OTHER']),
    phone: z.string().regex(/^0[35789]\d{8}$/, 'Số điện thoại không hợp lệ'),
    acceptTerms: z.literal(true, { error: 'Vui lòng đồng ý điều khoản' }),
  })
  .refine((d) => d.password === d.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Mật khẩu xác nhận không khớp',
  });
export type RegisterFormValues = z.infer<typeof registerSchema>;

export const verifyOtpSchema = z.object({
  otp: z.string().length(6, 'Mã OTP gồm 6 chữ số'),
});
export type VerifyOtpFormValues = z.infer<typeof verifyOtpSchema>;

/** Điểm mạnh mật khẩu 0-4 cho strength meter. */
export function passwordStrength(password: string): number {
  if (!password) return 0;
  let score = 0;
  if (password.length >= 8) score += 1;
  if (password.length >= 12) score += 1;
  if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score += 1;
  if (/\d/.test(password) && /[^A-Za-z0-9]/.test(password)) score += 1;
  return Math.min(score, 4);
}
