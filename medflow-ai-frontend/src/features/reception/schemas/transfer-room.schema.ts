import { z } from 'zod';

export const TRANSFER_REASON_CODES = [
  'TECHNICAL_FAILURE',
  'URGENT_CONSULTATION',
  'LOAD_BALANCING',
  'EXTRA_EXPERTISE',
  'OTHER',
] as const;

export const transferRoomSchema = z
  .object({
    targetRoomId: z.string().min(1, 'Vui lòng chọn buồng khám đích'),
    reasonCode: z.enum(TRANSFER_REASON_CODES, { message: 'Vui lòng chọn lý do chuyển buồng' }),
    note: z.string().max(500, 'Ghi chú không được vượt quá 500 ký tự').optional(),
  })
  .refine((v) => v.reasonCode !== 'OTHER' || (v.note?.trim().length ?? 0) > 0, {
    message: 'Vui lòng nhập ghi chú khi chọn lý do "Khác"',
    path: ['note'],
  });

export type TransferRoomFormValues = z.infer<typeof transferRoomSchema>;
