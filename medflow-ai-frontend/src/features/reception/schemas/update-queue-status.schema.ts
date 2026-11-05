import { z } from 'zod';

import { isAllowedQueueStatusTransition } from '../data/examination-queue-mock';
import {
  EXAMINATION_QUEUE_STATUS_VALUES,
  QUEUE_PRESENCE_STATUS_VALUES,
} from '../types/examination-queue.types';

export const updateQueueStatusSchema = z
  .object({
    queueEntryId: z.string().trim().min(1, 'Thiếu mã lượt hàng đợi.'),
    currentStatus: z.enum(EXAMINATION_QUEUE_STATUS_VALUES),
    targetStatus: z.enum(EXAMINATION_QUEUE_STATUS_VALUES, {
      message: 'Vui lòng chọn trạng thái chuyển tiếp hợp lệ.',
    }),
    presenceStatus: z.enum(QUEUE_PRESENCE_STATUS_VALUES, {
      message: 'Vui lòng chọn trạng thái hiện diện.',
    }),
    reason: z
      .string()
      .trim()
      .max(500, 'Lý do / ghi chú không được vượt quá 500 ký tự.')
      .refine((val) => !/[<>]/.test(val), 'Lý do / ghi chú chứa ký tự không hợp lệ.'),
  })
  .superRefine((data, ctx) => {
    if (!isAllowedQueueStatusTransition(data.currentStatus, data.targetStatus)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['targetStatus'],
        message: 'Chuyển tiếp trạng thái không hợp lệ đối với lượt hàng đợi hiện tại.',
      });
    }
    if (
      (data.targetStatus === 'CALLED' || data.targetStatus === 'IN_PROGRESS') &&
      data.presenceStatus !== 'PRESENT'
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['presenceStatus'],
        message: 'Chỉ có thể gọi số hoặc bắt đầu khám khi bệnh nhân đang có mặt (Có mặt).',
      });
    }
    if (data.targetStatus === 'SKIPPED' && data.reason.trim().length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['reason'],
        message: 'Vui lòng nhập lý do chuyển sang trạng thái Qua lượt.',
      });
    }
  });

export type UpdateQueueStatusFormValues = z.infer<typeof updateQueueStatusSchema>;
