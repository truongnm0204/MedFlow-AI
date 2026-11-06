import { z } from 'zod';

export const examinationQueueFilterSchema = z.object({
  queueDate: z
    .string()
    .trim()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Ngày hàng đợi không hợp lệ (định dạng YYYY-MM-DD).'),
  servicePointId: z.string().trim().min(1, 'Vui lòng chọn điểm dịch vụ / phòng khám.'),
  status: z.enum(['ALL', 'WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'COMPLETED', 'SKIPPED']),
  priority: z.enum(['ALL', 'NORMAL', 'URGENT', 'ELDERLY', 'PREGNANT', 'RETURN_RESULT']),
  visitType: z.enum(['ALL', 'SCHEDULED', 'WALK_IN']),
  arrivalClass: z.enum(['ALL', 'ON_TIME_OR_GRACE', 'EARLY', 'LATE', 'WALK_IN']).default('ALL'),
  keyword: z
    .string()
    .trim()
    .max(100, 'Từ khóa tìm kiếm không được vượt quá 100 ký tự.')
    .refine((val) => !/[<>]/.test(val), 'Từ khóa tìm kiếm chứa ký tự không hợp lệ.'),
});

export type ExaminationQueueFilterFormValues = z.infer<typeof examinationQueueFilterSchema>;
