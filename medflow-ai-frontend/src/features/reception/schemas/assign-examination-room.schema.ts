import { z } from 'zod';

export const VISIT_PRIORITY_VALUES = [
  'NORMAL',
  'URGENT',
  'ELDERLY',
  'PREGNANT',
  'RETURN_RESULT',
] as const;

export const assignExaminationRoomSchema = z.object({
  visitIdOrCode: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập mã lượt khám')
    .max(40, 'Mã lượt khám không hợp lệ'),
  roomCode: z.string().trim().min(1, 'Vui lòng chọn buồng khám lâm sàng'),
  priority: z.enum(VISIT_PRIORITY_VALUES),
  reason: z.string().trim().max(500, 'Lý do / ghi chú phân phòng không được vượt quá 500 ký tự'),
});

export type AssignExaminationRoomFormValues = z.infer<typeof assignExaminationRoomSchema>;
