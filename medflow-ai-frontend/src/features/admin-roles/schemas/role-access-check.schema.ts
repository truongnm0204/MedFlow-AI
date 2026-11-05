import { z } from 'zod';

export const roleAccessCheckSchema = z.object({
  roleCode: z.string().trim().min(1, 'Vui lòng chọn vai trò cần kiểm tra quyền truy cập.'),
  targetRoute: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập hoặc chọn đường dẫn màn hình cần kiểm tra.')
    .refine(
      (val) => val.startsWith('/') && !val.startsWith('//'),
      'Đường dẫn nội bộ phải bắt đầu bằng ký tự "/" hợp lệ (ví dụ: /admin/roles).',
    ),
  requiredPermission: z.string().trim().optional(),
});

export type RoleAccessCheckInput = z.infer<typeof roleAccessCheckSchema>;
