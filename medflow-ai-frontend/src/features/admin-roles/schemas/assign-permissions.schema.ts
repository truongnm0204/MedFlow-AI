import { z } from 'zod';

import { SYSTEM_PERMISSIONS } from '../data/role-mock-data';

export const VALID_PERMISSION_CODES = Object.keys(SYSTEM_PERMISSIONS) as [string, ...string[]];

export const assignPermissionsSchema = z
  .object({
    roleId: z.string().trim().min(1, 'Vui lòng chọn vai trò cần phân quyền.'),
    roleCode: z.string().trim().min(1, 'Mã vai trò không hợp lệ.'),
    permissionCodes: z
      .array(
        z.string().refine((code) => code in SYSTEM_PERMISSIONS, {
          message: 'Mã quyền hạn không nằm trong danh mục hệ thống.',
        }),
      )
      .min(1, 'Vai trò phải được gán ít nhất 1 quyền hạn hoạt động.'),
    note: z.string().max(250, 'Ghi chú kiểm toán không được vượt quá 250 ký tự.').optional(),
  })
  .superRefine((values, ctx) => {
    if (values.roleCode === 'SYSTEM_ADMIN' && !values.permissionCodes.includes('USER_MANAGE')) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['permissionCodes'],
        message:
          'Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.',
      });
    }

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

export type AssignPermissionsFormValues = z.infer<typeof assignPermissionsSchema>;
