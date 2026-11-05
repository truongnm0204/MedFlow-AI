import { z } from 'zod';

export const STAFF_ROLE_OPTIONS = [
  { value: 'DOCTOR', label: 'Bác sĩ' },
  { value: 'TECHNICIAN', label: 'Kỹ thuật viên' },
  { value: 'NURSE', label: 'Điều dưỡng' },
  { value: 'RECEPTIONIST', label: 'Nhân viên quầy' },
] as const;

export const STAFF_FACILITIES = ['MedFlow Cơ sở 1 - Hà Nội', 'MedFlow Cơ sở 2 - TP.HCM'] as const;

export const STAFF_DEPARTMENTS = [
  'Khoa Nội Tiêu hóa',
  'Khoa Tim mạch',
  'Khoa Khám bệnh',
  'Khoa CĐHA',
] as const;

export const staffCreateSchema = z
  .object({
    staffCode: z
      .string()
      .trim()
      .min(1, 'Vui lòng nhập mã nhân sự')
      .regex(/^STF-\d{4}$/, 'Mã nhân sự theo chuẩn STF-xxxx (VD: STF-0250)'),
    fullName: z
      .string()
      .trim()
      .min(2, 'Họ tên phải có ít nhất 2 ký tự')
      .max(120, 'Họ tên tối đa 120 ký tự'),
    authProvider: z.enum(['LOCAL', 'GOOGLE']),
    facility: z.string().min(1, 'Vui lòng chọn cơ sở'),
    email: z.string().trim().min(1, 'Vui lòng nhập email đăng nhập').email('Email không hợp lệ'),
    password: z.string(),
    status: z.enum(['ACTIVE', 'LOCKED']),
    role: z.enum(['DOCTOR', 'TECHNICIAN', 'NURSE', 'RECEPTIONIST'], {
      message: 'Vui lòng chọn vai trò',
    }),
    department: z.string().min(1, 'Vui lòng chọn khoa công tác'),
  })
  .superRefine((value, ctx) => {
    if (value.authProvider === 'LOCAL' && value.password.length < 8) {
      ctx.addIssue({
        code: 'custom',
        path: ['password'],
        message: 'Mật khẩu tạm thời tối thiểu 8 ký tự',
      });
    }
  });

export type StaffCreateFormValues = z.infer<typeof staffCreateSchema>;
