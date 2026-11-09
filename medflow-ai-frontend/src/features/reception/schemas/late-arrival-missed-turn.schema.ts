import { z } from 'zod';

const SAFE_TEXT_REGEX = /^[^<>]*$/;

export const lateArrivalMissedTurnSchema = z
  .object({
    mode: z.enum(['MISSED_TURN', 'RESTORE_TURN']),
    servicePointId: z.string().trim().min(1, 'Điểm phục vụ / phòng khám là bắt buộc.'),
    confirmPresent: z.boolean(),
    reason: z
      .string()
      .trim()
      .min(5, 'Lý do điều phối phải từ 5 đến 300 ký tự.')
      .max(300, 'Lý do điều phối phải từ 5 đến 300 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Lý do điều phối không được chứa ký tự < hoặc >.'),
    notes: z
      .string()
      .trim()
      .max(300, 'Ghi chú tối đa 300 ký tự.')
      .refine((v) => SAFE_TEXT_REGEX.test(v), 'Ghi chú không được chứa ký tự < hoặc >.'),
  })
  .superRefine((v, ctx) => {
    if (v.mode === 'RESTORE_TURN' && !v.confirmPresent) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['confirmPresent'],
        message: 'Cần xác nhận bệnh nhân đã có mặt (PRESENT) trước khi khôi phục lượt khám.',
      });
    }
  });

export type LateArrivalMissedTurnFormValues = z.infer<typeof lateArrivalMissedTurnSchema>;
