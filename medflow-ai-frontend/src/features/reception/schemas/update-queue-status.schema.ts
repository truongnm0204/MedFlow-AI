import { z } from 'zod';

const SAFE_TEXT_REGEX = /^[^<>]*$/;

export const updateQueueStatusSchema = z
  .object({
    queueEntryIdOrVisitCode: z
      .string()
      .trim()
      .min(1, 'Mã định danh lượt xếp hàng không hợp lệ.')
      .max(40, 'Mã định danh lượt xếp hàng không được vượt quá 40 ký tự.'),
    targetStatus: z.enum(['WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'COMPLETED', 'SKIPPED'], {
      error: 'Vui lòng chọn trạng thái hàng đợi đích hợp lệ.',
    }),
    presenceStatus: z.enum(['PRESENT', 'NOT_ARRIVED', 'LEFT']),
    reason: z
      .string()
      .trim()
      .max(500, 'Lý do cập nhật không được vượt quá 500 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Lý do không được chứa ký tự đặc biệt < hoặc >.'),
    notes: z
      .string()
      .trim()
      .max(500, 'Ghi chú không được vượt quá 500 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Ghi chú không được chứa ký tự đặc biệt < hoặc >.'),
  })
  .superRefine((values, ctx) => {
    if (values.targetStatus === 'SKIPPED' && values.reason.trim().length < 3) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['reason'],
        message: 'Vui lòng nhập lý do qua lượt (tối thiểu 3 ký tự).',
      });
    }
    if (
      values.targetStatus === 'IN_PROGRESS' &&
      (values.presenceStatus === 'NOT_ARRIVED' || values.presenceStatus === 'LEFT')
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['presenceStatus'],
        message: 'Không thể bắt đầu khám khi bệnh nhân chưa đến hoặc vắng mặt.',
      });
    }
  });

export type UpdateQueueStatusFormValues = z.infer<typeof updateQueueStatusSchema>;
