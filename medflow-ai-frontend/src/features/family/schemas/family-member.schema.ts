import { z } from 'zod';

function parseDateInput(str: string): Date | null {
  if (!str) return null;
  const parts = str.split(/[-/]/);
  if (parts.length === 3) {
    if (parts[2].length === 4) {
      const day = parseInt(parts[0], 10);
      const month = parseInt(parts[1], 10) - 1;
      const year = parseInt(parts[2], 10);
      const d = new Date(year, month, day);
      return isNaN(d.getTime()) ? null : d;
    }
    if (parts[0].length === 4) {
      const year = parseInt(parts[0], 10);
      const month = parseInt(parts[1], 10) - 1;
      const day = parseInt(parts[2], 10);
      const d = new Date(year, month, day);
      return isNaN(d.getTime()) ? null : d;
    }
  }
  const d = new Date(str);
  return isNaN(d.getTime()) ? null : d;
}

export const createFamilyMemberSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Họ và tên tối thiểu 2 ký tự')
    .max(100, 'Họ và tên không quá 100 ký tự'),
  relationship: z.enum(
    ['Bản thân', 'Con trai', 'Con gái', 'Vợ', 'Chồng', 'Mẹ ruột', 'Bố ruột', 'Khác'],
    { error: 'Vui lòng chọn mối quan hệ' },
  ),
  gender: z.enum(['Nam', 'Nữ'], { error: 'Vui lòng chọn giới tính' }),
  birthDate: z
    .string()
    .min(1, 'Vui lòng nhập ngày sinh')
    .refine(
      (v) => {
        const d = parseDateInput(v);
        return d !== null && d <= new Date();
      },
      {
        message: 'Ngày sinh không được lớn hơn ngày hiện tại',
      },
    ),
  identificationNo: z.string().trim().optional(),
  nationality: z.string().trim().default('Việt Nam'),
  phone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại liên hệ không hợp lệ (cần 10 chữ số bắt đầu bằng 0)',
    })
    .optional(),
  email: z
    .string()
    .trim()
    .refine((v) => !v || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), {
      message: 'Email nhận kết quả không hợp lệ',
    })
    .optional(),
  address: z.string().trim().min(5, 'Địa chỉ thường trú tối thiểu 5 ký tự'),
  emergencyContactName: z.string().trim().optional(),
  emergencyContactRelation: z.string().trim().optional(),
  emergencyPhone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại khẩn cấp không hợp lệ',
    })
    .optional(),
});

export type CreateFamilyMemberInput = z.infer<typeof createFamilyMemberSchema>;

export const updateFamilyMemberSchema = z.object({
  phone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại liên hệ không hợp lệ (cần 10 chữ số bắt đầu bằng 0)',
    })
    .optional(),
  email: z
    .string()
    .trim()
    .refine((v) => !v || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), {
      message: 'Email nhận kết quả không hợp lệ',
    })
    .optional(),
  address: z.string().trim().min(5, 'Địa chỉ thường trú tối thiểu 5 ký tự'),
  emergencyContactName: z
    .string()
    .trim()
    .min(2, 'Họ tên người liên hệ khẩn cấp tối thiểu 2 ký tự')
    .optional(),
  emergencyContactRelation: z.string().trim().optional(),
  emergencyPhone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại khẩn cấp không hợp lệ',
    })
    .optional(),
});

export type UpdateFamilyMemberInput = z.infer<typeof updateFamilyMemberSchema>;
