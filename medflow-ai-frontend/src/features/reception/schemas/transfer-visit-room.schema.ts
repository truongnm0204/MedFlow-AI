import { z } from 'zod';

export const transferVisitRoomSchema = z.object({
  visitIdOrCode: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập mã lượt khám')
    .max(40, 'Mã lượt khám không hợp lệ'),
  targetRoomCode: z
    .string()
    .trim()
    .min(1, 'Vui lòng chọn phòng khám đích tương thích')
    .max(40, 'Mã phòng khám đích không được vượt quá 40 ký tự'),
  reason: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập lý do chuyển phòng')
    .max(500, 'Lý do chuyển phòng không được vượt quá 500 ký tự'),
  notes: z.string().trim().max(500, 'Ghi chú chuyển phòng không được vượt quá 500 ký tự'),
});

export type TransferVisitRoomFormValues = z.infer<typeof transferVisitRoomSchema>;
