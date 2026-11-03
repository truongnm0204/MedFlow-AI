import { z } from 'zod';

export const roomUpdateSchema = z.object({
  name: z.string().trim().min(1, 'Vui lòng nhập tên phòng').max(100, 'Tên phòng tối đa 100 ký tự'),
  type: z.enum(['OUTPATIENT', 'PROCEDURAL']),
  department: z.string().trim().min(1, 'Vui lòng chọn khoa phụ trách'),
  location: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập vị trí phòng')
    .max(200, 'Vị trí phòng tối đa 200 ký tự'),
  capacity: z.number().int().min(1, 'Sức chứa tối thiểu 1 người').max(4, 'Sức chứa tối đa 4 người'),
  status: z.enum(['ACTIVE', 'MAINTENANCE', 'INACTIVE']),
});

export type RoomUpdateFormValues = z.infer<typeof roomUpdateSchema>;
