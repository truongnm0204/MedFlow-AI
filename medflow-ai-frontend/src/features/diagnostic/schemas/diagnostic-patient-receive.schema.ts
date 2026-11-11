import { z } from 'zod';

export const RECEPTION_NOTE_PRESETS = [
  'Bệnh nhân đã có mặt tại điểm dịch vụ, đối chiếu đúng họ tên & mã BN, sẵn sàng vào hàng đợi.',
  'Đã kiểm tra điều kiện nhịn ăn / chuẩn bị trước kỹ thuật đạt yêu cầu, chuyển trạng thái READY.',
  'Đã xác minh tiền sử dị ứng / an toàn thuốc cản quang, hướng dẫn bệnh nhân ngồi chờ gọi tên.',
  'Bệnh nhân ưu tiên lâm sàng đã đến phòng kỹ thuật, xếp sẵn sàng phục vụ sớm.',
] as const;

export const diagnosticPatientReceivePrioritySchema = z
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

export const diagnosticManualPrioritySchema = z
  .string()
  .trim()
  .transform((value) => value.toUpperCase())
  .refine(
    (value) => value === 'NORMAL' || value === 'ELDERLY' || value === 'PREGNANT',
    'Mức độ ưu tiên lâm sàng không hợp lệ. Chỉ chấp nhận NORMAL, ELDERLY hoặc PREGNANT.',
  );

export const receiveDiagnosticPatientSchema = z.object({
  isPatientPresent: z
    .boolean()
    .refine(
      (value) => value === true,
      'Nhân viên cận lâm sàng cần xác nhận bệnh nhân đã có mặt tại điểm dịch vụ (isPatientPresent = true).',
    ),
  isIdentityVerified: z
    .boolean()
    .refine(
      (value) => value === true,
      'Nhân viên cận lâm sàng cần xác nhận đã đối chiếu thông tin bệnh nhân (isIdentityVerified = true).',
    ),
  isPreparationVerified: z
    .boolean()
    .refine(
      (value) => value === true,
      'Nhân viên cận lâm sàng cần xác nhận bệnh nhân đủ điều kiện chuẩn bị thực hiện kỹ thuật (isPreparationVerified = true).',
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
  priority: diagnosticPatientReceivePrioritySchema.nullable().optional(),
  manualPriority: diagnosticManualPrioritySchema.nullable().optional(),
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
  receptionNote: z
    .string()
    .trim()
    .max(500, 'Ghi chú tiếp nhận bệnh nhân không được vượt quá 500 ký tự.')
    .refine(
      (value) => !value.includes('<') && !value.includes('>'),
      'Ghi chú tiếp nhận bệnh nhân chứa ký tự không hợp lệ.',
    )
    .nullable()
    .optional(),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái hàng đợi (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const receiveDiagnosticPatientsBatchSchema = receiveDiagnosticPatientSchema
  .extend({
    orderIds: z.array(z.number().int().min(1)).optional(),
    orderCodes: z.array(z.string().trim().min(1)).optional(),
    visitId: z.number().int().min(1).nullable().optional(),
    visitCode: z.string().trim().max(40).nullable().optional(),
  })
  .superRefine((data, ctx) => {
    const rawIds = data.orderIds ?? [];
    const rawCodes = (data.orderCodes ?? []).map((code) => code.trim().toUpperCase());
    const hasVisitTarget = Boolean(data.visitId || (data.visitCode && data.visitCode.trim()));
    const totalCount = rawIds.length + rawCodes.length;

    if (totalCount === 0 && !hasVisitTarget) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để tiếp nhận bệnh nhân.',
      });
      return;
    }

    if (totalCount > 20) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Mỗi lần chỉ được tiếp nhận tối đa 20 phiếu chỉ định cận lâm sàng.',
      });
    }

    const uniqueIds = new Set(rawIds);
    const uniqueCodes = new Set(rawCodes);
    if (uniqueIds.size !== rawIds.length || uniqueCodes.size !== rawCodes.length) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['orderIds'],
        message: 'Danh sách phiếu chỉ định cần tiếp nhận có mã hoặc ID bị trùng lặp.',
      });
    }
  });

export type ReceiveDiagnosticPatientFormValues = z.infer<typeof receiveDiagnosticPatientSchema>;
export type ReceiveDiagnosticPatientsBatchFormValues = z.infer<
  typeof receiveDiagnosticPatientsBatchSchema
>;
