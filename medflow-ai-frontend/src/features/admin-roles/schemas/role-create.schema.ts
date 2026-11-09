import { z } from 'zod';

import { SYSTEM_PERMISSIONS } from '../data/role-mock-data';

export const ROLE_CODE_REGEX = /^[A-Z][A-Z0-9_]{1,49}$/;

export const ROLE_SCOPE_VALUES = ['SYSTEM', 'CLINICAL', 'OPERATIONAL', 'PORTAL'] as const;

export const ROLE_STATUS_VALUES = ['ACTIVE', 'INACTIVE'] as const;

export const createRoleSchema = z
  .object({
    code: z
      .string()
      .trim()
      .min(1, 'Mã vai trò không được để trống.')
      .transform((val) => val.toUpperCase())
      .refine((val) => ROLE_CODE_REGEX.test(val), {
        message:
          'Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).',
      }),
    name: z
      .string()
      .trim()
      .min(1, 'Tên vai trò không được để trống.')
      .min(2, 'Tên vai trò phải từ 2 đến 120 ký tự.')
      .max(120, 'Tên vai trò phải từ 2 đến 120 ký tự.'),
    scope: z
      .enum(ROLE_SCOPE_VALUES, {
        message:
          'Nhóm phân hệ vai trò (scope) không hợp lệ. Chỉ chấp nhận SYSTEM, CLINICAL, OPERATIONAL hoặc PORTAL.',
      })
      .default('OPERATIONAL'),
    status: z
      .enum(ROLE_STATUS_VALUES, {
        message: 'Trạng thái vai trò (status) không hợp lệ. Chỉ chấp nhận ACTIVE hoặc INACTIVE.',
      })
      .default('ACTIVE'),
    description: z
      .string()
      .trim()
      .max(500, 'Mô tả vai trò không được vượt quá 500 ký tự.')
      .optional(),
    segregationNote: z
      .string()
      .trim()
      .max(500, 'Ghi chú phân tách nhiệm vụ (SoD) không được vượt quá 500 ký tự.')
      .optional(),
    permissionCodes: z
      .array(
        z.string().refine((code) => code in SYSTEM_PERMISSIONS, {
          message: 'Mã quyền hạn không nằm trong danh mục hệ thống.',
        }),
      )
      .default([]),
    note: z.string().trim().max(250, 'Ghi chú kiểm toán không được vượt quá 250 ký tự.').optional(),
  })
  .superRefine((values, ctx) => {
    if (
      values.permissionCodes.includes('PRESCRIPTION_ISSUE') &&
      values.permissionCodes.includes('MEDICINE_DISPENSE')
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['permissionCodes'],
        message:
          'Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.',
      });
    }
  });

export type CreateRoleFormValues = z.infer<typeof createRoleSchema>;
