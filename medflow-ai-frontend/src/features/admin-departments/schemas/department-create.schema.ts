import { z } from 'zod';

export const DEPARTMENT_STATUS_VALUES = ['ACTIVE', 'INACTIVE'] as const;

export const departmentCreateSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập mã khoa phòng')
    .regex(/^DEP-\d{3,}$/, 'Mã khoa phòng theo chuẩn DEP-xxx (VD: DEP-015)'),
  name: z
    .string()
    .trim()
    .min(3, 'Tên khoa phòng phải có ít nhất 3 ký tự')
    .max(200, 'Tên khoa phòng tối đa 200 ký tự'),
  facilityId: z.string().min(1, 'Vui lòng chọn cơ sở'),
  status: z.enum(DEPARTMENT_STATUS_VALUES, { message: 'Vui lòng chọn trạng thái hợp lệ' }),
});

export type DepartmentCreateFormValues = z.infer<typeof departmentCreateSchema>;
