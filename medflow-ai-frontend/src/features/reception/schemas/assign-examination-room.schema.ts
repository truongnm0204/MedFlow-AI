import { z } from 'zod';

import { EXAMINATION_QUEUE_PRIORITY_VALUES } from '../types/examination-queue.types';

export const assignExaminationRoomSchema = z
  .object({
    visitIdOrCode: z
      .string()
      .trim()
      .min(1, 'Vui lòng nhập mã lượt khám')
      .max(40, 'Mã lượt khám không hợp lệ'),
    roomCode: z.string().trim().min(1, 'Vui lòng chọn buồng khám lâm sàng'),
    priority: z.enum(EXAMINATION_QUEUE_PRIORITY_VALUES),
    initialPriority: z.enum(EXAMINATION_QUEUE_PRIORITY_VALUES),
    priorityVerificationConfirmed: z.boolean(),
    reason: z.string().trim().max(500, 'Lý do / ghi chú phân phòng không được vượt quá 500 ký tự'),
  })
  .superRefine((values, ctx) => {
    if (values.priority !== 'NORMAL' && values.priority !== values.initialPriority) {
      if (values.reason.trim().length === 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['reason'],
          message: 'Vui lòng nhập lý do khi áp dụng mức ưu tiên ELDERLY/PREGNANT.',
        });
      }
      if (!values.priorityVerificationConfirmed) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['priorityVerificationConfirmed'],
          message: 'Vui lòng xác nhận đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên.',
        });
      }
    }
  });

export type AssignExaminationRoomFormValues = z.infer<typeof assignExaminationRoomSchema>;
