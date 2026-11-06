import { z } from 'zod';

export function parseDateInput(str: string): { date: Date | null; error?: string } {
  if (!str || !str.trim()) {
    return { date: null, error: 'Vui lòng nhập ngày sinh' };
  }
  const trimmed = str.trim();

  // Kiểm tra định dạng có đúng 4 chữ số năm: DD/MM/YYYY hoặc YYYY-MM-DD
  const dmyMatch = trimmed.match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/);
  const ymdMatch = trimmed.match(/^(\d{4})[/.-](\d{1,2})[/.-](\d{1,2})$/);

  if (!dmyMatch && !ymdMatch) {
    return {
      date: null,
      error: 'Định dạng ngày sinh phải là DD/MM/YYYY (ví dụ: 15/08/2018), năm sinh gồm 4 chữ số',
    };
  }

  let day: number, month: number, year: number;
  if (dmyMatch) {
    day = parseInt(dmyMatch[1], 10);
    month = parseInt(dmyMatch[2], 10) - 1; // 0-based
    year = parseInt(dmyMatch[3], 10);
  } else {
    year = parseInt(ymdMatch![1], 10);
    month = parseInt(ymdMatch![2], 10) - 1;
    day = parseInt(ymdMatch![3], 10);
  }

  if (year < 1900) {
    return { date: null, error: 'Năm sinh không hợp lệ (năm sinh từ năm 1900 đến nay)' };
  }

  const d = new Date(year, month, day);
  if (d.getFullYear() !== year || d.getMonth() !== month || d.getDate() !== day) {
    return {
      date: null,
      error: 'Ngày sinh không tồn tại trên lịch (vui lòng kiểm tra lại ngày và tháng sinh)',
    };
  }

  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 23, 59, 59, 999);
  if (d > today) {
    return { date: null, error: 'Ngày sinh không được lớn hơn ngày hiện tại' };
  }

  return { date: d };
}

export const createFamilyMemberSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Họ và tên tối thiểu 2 ký tự')
    .max(100, 'Họ và tên không quá 100 ký tự')
    .refine((v) => /^[\p{L}\s.'-]+$/u.test(v), {
      message: 'Họ và tên chỉ được chứa chữ cái và khoảng trắng',
    }),
  relationship: z
    .enum(['Con trai', 'Con gái', 'Vợ', 'Chồng', 'Mẹ ruột', 'Bố ruột', 'Khác'], {
      error: 'Vui lòng chọn mối quan hệ',
    })
    .refine((v) => v !== ('Bản thân' as string), {
      message: 'Không thể tạo hồ sơ người thân với mối quan hệ là Bản thân',
    }),
  gender: z.enum(['Nam', 'Nữ'], { error: 'Vui lòng chọn giới tính' }),
  birthDate: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập ngày sinh')
    .superRefine((v, ctx) => {
      const res = parseDateInput(v);
      if (!res.date || res.error) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: res.error ?? 'Ngày sinh không hợp lệ',
        });
      }
    }),
  identificationNo: z
    .string()
    .trim()
    .refine((v) => !v || /^\d{9}(\d{3})?$/.test(v), {
      message: 'Số CCCD/định danh không hợp lệ (cần 9 hoặc 12 chữ số)',
    })
    .optional(),
  nationality: z.string().trim().default('Việt Nam'),
  phone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$|^0\d{9}$/.test(v.replace(/\s+/g, '')), {
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
  address: z
    .string()
    .trim()
    .min(5, 'Địa chỉ thường trú tối thiểu 5 ký tự')
    .max(255, 'Địa chỉ thường trú không quá 255 ký tự'),
  emergencyContactName: z
    .string()
    .trim()
    .refine((v) => !v || /^[\p{L}\s.'-]+$/u.test(v), {
      message: 'Họ tên người liên hệ khẩn cấp chỉ được chứa chữ cái và khoảng trắng',
    })
    .optional(),
  emergencyContactRelation: z.string().trim().optional(),
  emergencyPhone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$|^0\d{9}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại khẩn cấp không hợp lệ (cần 10 chữ số bắt đầu bằng 0)',
    })
    .optional(),
});

export type CreateFamilyMemberInput = z.infer<typeof createFamilyMemberSchema>;

export const updateFamilyMemberSchema = z.object({
  phone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$|^0\d{9}$/.test(v.replace(/\s+/g, '')), {
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
  address: z
    .string()
    .trim()
    .min(5, 'Địa chỉ thường trú tối thiểu 5 ký tự')
    .max(255, 'Địa chỉ thường trú không quá 255 ký tự'),
  emergencyContactName: z
    .string()
    .trim()
    .min(2, 'Họ tên người liên hệ khẩn cấp tối thiểu 2 ký tự')
    .refine((v) => !v || /^[\p{L}\s.'-]+$/u.test(v), {
      message: 'Họ tên người liên hệ khẩn cấp chỉ được chứa chữ cái và khoảng trắng',
    })
    .optional(),
  emergencyContactRelation: z.string().trim().optional(),
  emergencyPhone: z
    .string()
    .trim()
    .refine((v) => !v || /^0[35789]\d{8}$|^0\d{9}$/.test(v.replace(/\s+/g, '')), {
      message: 'Số điện thoại khẩn cấp không hợp lệ (cần 10 chữ số bắt đầu bằng 0)',
    })
    .optional(),
});

export type UpdateFamilyMemberInput = z.infer<typeof updateFamilyMemberSchema>;
