import { z } from 'zod';

import { EXAMINATION_QUEUE_PRIORITY_VALUES } from '../types/examination-queue.types';

const SAFE_TEXT_REGEX = /^[^<>]*$/;

export const applyClinicalPrioritySchema = z
  .object({
    priority: z.enum(EXAMINATION_QUEUE_PRIORITY_VALUES, {
      error: 'Vui lòng chọn mức ưu tiên khám ngoại trú.',
    }),
    reason: z
      .string()
      .trim()
      .min(1, 'Vui lòng nhập lý do xác minh đối tượng ưu tiên.')
      .max(500, 'Lý do áp dụng mức ưu tiên không được vượt quá 500 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Lý do không được chứa ký tự đặc biệt < hoặc >.'),
    verificationConfirmed: z.boolean(),
  })
  .superRefine((v, ctx) => {
    if (v.priority !== 'NORMAL' && !v.verificationConfirmed) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['verificationConfirmed'],
        message: 'Vui lòng xác nhận đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên.',
      });
    }
  });

export type ApplyClinicalPriorityFormValues = z.infer<typeof applyClinicalPrioritySchema>;
