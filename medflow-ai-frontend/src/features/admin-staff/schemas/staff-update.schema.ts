import { z } from 'zod';

export const staffUpdateSchema = z.object({
  fullName: z.string().trim().min(1, 'Vui lòng nhập họ tên nhân sự').max(120, 'Tối đa 120 ký tự'),
  authMethod: z.enum(['LOCAL', 'GOOGLE']),
  facility: z.string().min(1, 'Vui lòng chọn cơ sở'),
  email: z.string().trim().min(1, 'Vui lòng nhập email đăng nhập').email('Email không hợp lệ'),
  status: z.enum(['ACTIVE', 'LOCKED', 'DISABLED']),
  role: z.enum(['DOCTOR', 'TECHNICIAN', 'NURSE', 'RECEPTIONIST']),
  department: z.string().min(1, 'Vui lòng chọn khoa công tác'),
});

export type StaffUpdateFormValues = z.infer<typeof staffUpdateSchema>;
