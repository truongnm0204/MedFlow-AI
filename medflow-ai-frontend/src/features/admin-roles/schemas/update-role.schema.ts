import { z } from 'zod';

export const ROLE_CODE_REGEX = /^[A-Z][A-Z0-9_]{1,49}$/;

export const updateRoleSchema = z
  .object({
    roleId: z.string().trim().min(1, 'Vui lòng chọn vai trò cần cập nhật.'),
    originalCode: z.string().trim().min(1, 'Mã vai trò gốc không hợp lệ.'),
    isSystemRole: z.boolean(),
    code: z
      .string()
      .trim()
      .min(1, 'Mã vai trò không hợp lệ.')
      .transform((val) => val.toUpperCase())
      .refine((val) => ROLE_CODE_REGEX.test(val), {
        message:
          'Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).',
      }),
    name: z
      .string()
      .trim()
      .min(1, 'Tên vai trò không được để trống.')
      .refine((val) => val.length >= 2 && val.length <= 120, {
        message: 'Tên vai trò phải từ 2 đến 120 ký tự.',
      }),
    scope: z.enum(['SYSTEM', 'CLINICAL', 'OPERATIONAL', 'PORTAL'], {
      message:
        'Nhóm phân hệ vai trò (scope) không hợp lệ. Chỉ chấp nhận SYSTEM, CLINICAL, OPERATIONAL hoặc PORTAL.',
    }),
    status: z.enum(['ACTIVE', 'INACTIVE'], {
      message: 'Trạng thái vai trò (status) không hợp lệ. Chỉ chấp nhận ACTIVE hoặc INACTIVE.',
    }),
    description: z
      .string()
      .trim()
      .min(1, 'Mô tả vai trò không được để trống.')
      .max(500, 'Mô tả vai trò không được vượt quá 500 ký tự.'),
    segregationNote: z
      .string()
      .trim()
      .min(1, 'Ghi chú phân tách nhiệm vụ (SoD) không được để trống.')
      .max(500, 'Ghi chú phân tách nhiệm vụ (SoD) không được vượt quá 500 ký tự.'),
    note: z.string().trim().max(250, 'Ghi chú kiểm toán không được vượt quá 250 ký tự.').optional(),
  })
  .superRefine((values, ctx) => {
    const normalizedOriginalCode = values.originalCode.trim().toUpperCase();
    const normalizedCode = values.code.trim().toUpperCase();

    if (values.isSystemRole && normalizedCode !== normalizedOriginalCode) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['code'],
        message: 'Không được phép thay đổi mã định danh (code) của vai trò hệ thống mặc định.',
      });
    }

    if (values.status === 'INACTIVE') {
      if (normalizedOriginalCode === 'SYSTEM_ADMIN') {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['status'],
          message:
            'Không thể ngừng hoạt động vai trò SYSTEM_ADMIN để tránh khóa quyền quản trị hệ thống.',
        });
      } else if (values.isSystemRole) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['status'],
          message: 'Không thể ngừng hoạt động vai trò hệ thống mặc định.',
        });
      }
    }

    if (normalizedOriginalCode === 'SYSTEM_ADMIN' && values.scope !== 'SYSTEM') {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['scope'],
        message: 'Vai trò SYSTEM_ADMIN bắt buộc thuộc nhóm phân hệ SYSTEM.',
      });
    }
  });

export type UpdateRoleFormValues = z.infer<typeof updateRoleSchema>;
