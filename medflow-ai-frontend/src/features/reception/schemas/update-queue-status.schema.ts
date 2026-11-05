import { z } from 'zod';

import { isAllowedQueueStatusTransition } from '../data/examination-queue-mock';
import {
  EXAMINATION_QUEUE_STATUS_VALUES,
  QUEUE_PRESENCE_STATUS_VALUES,
} from '../types/examination-queue.types';

const SAFE_TEXT_REGEX = /^[^<>]*$/;

export const updateQueueStatusSchema = z
  .object({
    queueEntryIdOrVisitCode: z
      .string()
      .trim()
      .min(1, 'Mã định danh lượt xếp hàng không hợp lệ.')
      .max(40, 'Mã định danh lượt xếp hàng không được vượt quá 40 ký tự.'),
    currentStatus: z.enum(EXAMINATION_QUEUE_STATUS_VALUES).optional(),
    targetStatus: z.enum(EXAMINATION_QUEUE_STATUS_VALUES, {
      error: 'Vui lòng chọn trạng thái hàng đợi đích hợp lệ.',
    }),
    presenceStatus: z.enum(QUEUE_PRESENCE_STATUS_VALUES, {
      error: 'Vui lòng chọn trạng thái hiện diện.',
    }),
    reason: z
      .string()
      .trim()
      .max(500, 'Lý do cập nhật không được vượt quá 500 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Lý do không được chứa ký tự đặc biệt < hoặc >.'),
    notes: z
      .string()
      .trim()
      .max(500, 'Ghi chú không được vượt quá 500 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Ghi chú không được chứa ký tự đặc biệt < hoặc >.')
      .optional()
      .default(''),
  })
  .superRefine((values, ctx) => {
    if (
      values.currentStatus &&
      !isAllowedQueueStatusTransition(values.currentStatus, values.targetStatus)
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['targetStatus'],
        message: 'Chuyển tiếp trạng thái không hợp lệ đối với lượt hàng đợi hiện tại.',
      });
    }

    if (values.targetStatus === 'SKIPPED' && values.reason.trim().length < 3) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['reason'],
        message: 'Vui lòng nhập lý do qua lượt (tối thiểu 3 ký tự).',
      });
    }

    if (
      (values.targetStatus === 'CALLED' || values.targetStatus === 'IN_PROGRESS') &&
      values.presenceStatus !== 'PRESENT'
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['presenceStatus'],
        message:
          values.targetStatus === 'IN_PROGRESS' && values.presenceStatus === 'LEFT'
            ? 'Không thể bắt đầu khám khi bệnh nhân chưa đến hoặc vắng mặt.'
            : 'Chỉ có thể gọi số hoặc bắt đầu khám khi bệnh nhân đang có mặt (Có mặt).',
      });
    }
  });

export type UpdateQueueStatusFormValues = z.infer<typeof updateQueueStatusSchema>;
