import { z } from 'zod';

export const earlyArrivalBackfillSchema = z
  .object({
    queueEntryId: z.string().trim().min(1, 'Thiếu mã lượt hàng đợi.'),
    servicePointId: z.string().trim().min(1, 'Thiếu mã điểm dịch vụ / phòng khám.'),
    visitType: z.enum(['SCHEDULED', 'WALK_IN']),
    actionMode: z.enum(['SAFE_BACKFILL', 'HOLD_FOR_SLOT']),
    arrivalClass: z.enum(['ON_TIME_OR_GRACE', 'EARLY', 'LATE', 'WALK_IN']),
    reason: z
      .string()
      .trim()
      .min(5, 'Vui lòng nhập lý do xử lý tối thiểu 5 ký tự.')
      .max(300, 'Lý do xử lý không được vượt quá 300 ký tự.')
      .refine((val) => !/[<>]/.test(val), 'Lý do xử lý chứa ký tự không hợp lệ.'),
    notes: z
      .string()
      .trim()
      .max(300, 'Ghi chú bổ sung không được vượt quá 300 ký tự.')
      .refine((val) => !/[<>]/.test(val), 'Ghi chú bổ sung chứa ký tự không hợp lệ.'),
    acknowledgeAppointmentProtection: z.boolean(),
  })
  .superRefine((val, ctx) => {
    if (val.actionMode === 'SAFE_BACKFILL' && !val.acknowledgeAppointmentProtection) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['acknowledgeAppointmentProtection'],
        message:
          'Vui lòng xác nhận cam kết bảo vệ lịch hẹn đúng giờ và giữ nguyên số thứ tự trước khi Safe Backfill.',
      });
    }
    if (val.actionMode === 'HOLD_FOR_SLOT' && val.visitType === 'WALK_IN') {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['actionMode'],
        message:
          'Bệnh nhân vãng lai (Walk-in) không có khung giờ hẹn cố định để giữ chờ theo slot.',
      });
    }
  });

export type EarlyArrivalBackfillFormValues = z.infer<typeof earlyArrivalBackfillSchema>;
