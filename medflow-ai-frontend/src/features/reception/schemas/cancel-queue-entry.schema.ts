import { z } from 'zod';

const SAFE_TEXT_REGEX = /^[^<>]*$/;

export const cancelQueueEntrySchema = z.object({
  reason: z
    .string()
    .trim()
    .min(5, 'Lý do hủy lượt phải từ 5 đến 500 ký tự.')
    .max(500, 'Lý do hủy lượt phải từ 5 đến 500 ký tự.')
    .refine((v) => SAFE_TEXT_REGEX.test(v), 'Lý do hủy lượt không được chứa ký tự < hoặc >.'),
  notes: z
    .string()
    .trim()
    .max(500, 'Ghi chú tối đa 500 ký tự.')
    .refine((v) => SAFE_TEXT_REGEX.test(v), 'Ghi chú không được chứa ký tự < hoặc >.'),
});

export type CancelQueueEntryFormValues = z.infer<typeof cancelQueueEntrySchema>;
