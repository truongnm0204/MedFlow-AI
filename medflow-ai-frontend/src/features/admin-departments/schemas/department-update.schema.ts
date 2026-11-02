import { z } from 'zod';

export const DEPARTMENT_FACILITIES = [
  'MedFlow Cơ sở 1 - Hà Nội',
  'MedFlow Cơ sở 2 - TP.HCM',
] as const;

export const departmentUpdateSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập tên khoa phòng')
    .max(150, 'Tên khoa phòng tối đa 150 ký tự'),
  facility: z.enum(DEPARTMENT_FACILITIES, { error: 'Vui lòng chọn cơ sở' }),
  status: z.enum(['ACTIVE', 'INACTIVE'], { error: 'Vui lòng chọn trạng thái' }),
});

export type DepartmentUpdateFormValues = z.infer<typeof departmentUpdateSchema>;
