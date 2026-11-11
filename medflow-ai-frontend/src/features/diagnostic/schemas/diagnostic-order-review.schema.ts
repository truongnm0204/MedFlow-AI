import { z } from 'zod';

export const CONFIRMATION_NOTE_PRESETS = [
  'Đã đối chiếu thông tin hành chính và chỉ định lâm sàng hợp lệ.',
  'Đã hướng dẫn bệnh nhân nhịn ăn/chuẩn bị trước kỹ thuật và xếp hàng đợi.',
  'Ưu tiên thực hiện sớm theo yêu cầu lâm sàng của bác sĩ chỉ định.',
  'Đã kiểm tra chống chỉ định/tiền sử dị ứng và phân luồng phòng thực hiện.',
] as const;

export const diagnosticOrderConfirmPrioritySchema = z
  .string()
  .trim()
  .transform((value) => {
    const upper = value.toUpperCase();
    if (upper === 'STANDARD' || upper === 'ROUTINE') {
      return 'NORMAL';
    }
    return upper;
  })
  .refine(
    (value) => value === 'NORMAL' || value === 'URGENT' || value === 'STAT',
    'Mức độ ưu tiên chỉ định cận lâm sàng không hợp lệ. Chỉ chấp nhận NORMAL, URGENT hoặc STAT.',
  );

export const confirmDiagnosticOrderSchema = z.object({
  isEligibilityVerified: z
    .boolean()
    .refine(
      (value) => value === true,
      'Nhân viên cận lâm sàng cần xác nhận phiếu chỉ định đủ điều kiện xử lý (isEligibilityVerified = true).',
    ),
  servicePointId: z
    .number()
    .int()
    .min(1, 'ID điểm phục vụ cận lâm sàng (ServicePointId) phải lớn hơn 0.')
    .nullable()
    .optional(),
  roomId: z
    .number()
    .int()
    .min(1, 'ID phòng thực hiện cận lâm sàng (RoomId) phải lớn hơn 0.')
    .nullable()
    .optional(),
  priority: diagnosticOrderConfirmPrioritySchema.nullable().optional(),
  instruction: z
    .string()
    .trim()
    .max(1000, 'Hướng dẫn thực hiện cận lâm sàng không được vượt quá 1000 ký tự.')
    .refine(
      (value) => !value.includes('<') && !value.includes('>'),
      'Hướng dẫn thực hiện cận lâm sàng chứa ký tự không hợp lệ.',
    )
    .nullable()
    .optional(),
  confirmationNote: z
    .string()
    .trim()
    .max(500, 'Ghi chú xác nhận chỉ định không được vượt quá 500 ký tự.')
    .refine(
      (value) => !value.includes('<') && !value.includes('>'),
      'Ghi chú xác nhận chỉ định chứa ký tự không hợp lệ.',
    )
    .nullable()
    .optional(),
  patientPresent: z.boolean().nullable().optional(),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái hàng đợi (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const confirmDiagnosticOrdersBatchSchema = confirmDiagnosticOrderSchema
  .extend({
    orderIds: z.array(z.number().int().min(1)).optional(),
    orderCodes: z.array(z.string().trim().min(1)).optional(),
  })
  .superRefine((data, ctx) => {
    const rawIds = data.orderIds ?? [];
    const rawCodes = (data.orderCodes ?? []).map((code) => code.trim().toUpperCase());
    const totalCount = rawIds.length + rawCodes.length;

    if (totalCount === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để xác nhận.',
      });
      return;
    }

    if (totalCount > 20) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Mỗi lần chỉ được xác nhận tối đa 20 phiếu chỉ định cận lâm sàng.',
      });
    }

    const uniqueIds = new Set(rawIds);
    const uniqueCodes = new Set(rawCodes);
    if (uniqueIds.size !== rawIds.length || uniqueCodes.size !== rawCodes.length) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Danh sách phiếu chỉ định cần xác nhận có mã hoặc ID bị trùng lặp.',
      });
    }
  });

export type ConfirmDiagnosticOrderFormValues = z.infer<typeof confirmDiagnosticOrderSchema>;
export type ConfirmDiagnosticOrdersBatchFormValues = z.infer<
  typeof confirmDiagnosticOrdersBatchSchema
>;
