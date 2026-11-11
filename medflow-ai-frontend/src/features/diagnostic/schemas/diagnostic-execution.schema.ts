import { z } from 'zod';

// Lý do tạm dừng / hủy thực hiện kỹ thuật (5–500 ký tự) — ADR 0034 §3.
export const EXECUTION_REASON_PRESETS = [
  'Bệnh nhân chưa sẵn sàng thực hiện kỹ thuật, tạm dừng và xếp lại hàng đợi.',
  'Phòng kỹ thuật đang bận / thiết bị bảo trì, phiếu chỉ định tạm hoãn.',
  'Cần bổ sung điều kiện chuẩn bị (nhịn ăn, ngừng thuốc), tạm dừng thực hiện.',
  'Bệnh nhân xin hoãn kỹ thuật, đợi xác nhận lại từ bác sĩ chỉ định.',
] as const;

export const CANCEL_EXECUTION_REASON_PRESETS = [
  'Bác sĩ chỉ định hủy kỹ thuật, phiếu chỉ định không còn hiệu lực.',
  'Bệnh nhân từ chối thực hiện kỹ thuật sau tư vấn.',
  'Sai chỉ định / trùng phiếu, hủy theo yêu cầu điều phối.',
  'Bệnh nhân không đủ điều kiện sức khỏe thực hiện kỹ thuật.',
] as const;

export const DIAGNOSTIC_EXECUTION_TARGET_STATUSES = [
  'IN_PROGRESS',
  'COMPLETED',
  'CONFIRMED',
  'READY',
  'WAITING',
  'CANCELLED',
] as const;

const PAUSE_OR_CANCEL_TARGETS: Record<string, true> = {
  CONFIRMED: true,
  READY: true,
  WAITING: true,
  CANCELLED: true,
};

function isSafeText(value: string): boolean {
  return !value.includes('<') && !value.includes('>');
}

const executionReasonSchema = z
  .string()
  .trim()
  .max(500, 'Lý do cập nhật trạng thái thực hiện không được vượt quá 500 ký tự.')
  .refine(isSafeText, 'Lý do cập nhật trạng thái thực hiện chứa ký tự không hợp lệ.')
  .nullable()
  .optional();

const technicalFieldSchema = (max: number, label: string) =>
  z
    .string()
    .trim()
    .max(max, `${label} không hợp lệ (tối đa ${max} ký tự).`)
    .refine(isSafeText, `${label} chứa ký tự không hợp lệ.`)
    .nullable()
    .optional();

export const updateDiagnosticServiceStatusSchema = z
  .object({
    targetStatus: z.enum(DIAGNOSTIC_EXECUTION_TARGET_STATUSES, {
      message: 'Trạng thái thực hiện kỹ thuật đích không hợp lệ.',
    }),
    reason: executionReasonSchema,
    specimenCode: technicalFieldSchema(50, 'Mã bệnh phẩm'),
    specimenType: technicalFieldSchema(100, 'Loại bệnh phẩm'),
    equipmentCode: technicalFieldSchema(50, 'Mã thiết bị'),
    executionNote: technicalFieldSchema(200, 'Ghi chú thực hiện kỹ thuật'),
    expectedStateVersion: z
      .number()
      .int('Phiên bản trạng thái (ExpectedStateVersion) phải là số nguyên.')
      .nonnegative('Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 0.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    const trimmedReason = data.reason?.trim() ?? '';
    if (PAUSE_OR_CANCEL_TARGETS[data.targetStatus] === true) {
      if (trimmedReason.length < 5 || trimmedReason.length > 500) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['reason'],
          message: 'Lý do tạm dừng / hủy thực hiện kỹ thuật phải từ 5 đến 500 ký tự.',
        });
      }
    }
  });

export const updateDiagnosticServiceStatusBatchSchema = z
  .object({
    orderIds: z
      .array(z.number().int().min(1))
      .min(1, 'Chọn ít nhất 1 phiếu chỉ định để cập nhật hàng loạt.'),
    targetStatus: z.enum(DIAGNOSTIC_EXECUTION_TARGET_STATUSES, {
      message: 'Trạng thái thực hiện kỹ thuật đích không hợp lệ.',
    }),
    reason: executionReasonSchema,
    expectedStateVersion: z
      .number()
      .int('Phiên bản trạng thái (ExpectedStateVersion) phải là số nguyên.')
      .nonnegative('Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 0.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    const trimmedReason = data.reason?.trim() ?? '';
    if (PAUSE_OR_CANCEL_TARGETS[data.targetStatus] === true) {
      if (trimmedReason.length < 5 || trimmedReason.length > 500) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['reason'],
          message: 'Lý do tạm dừng / hủy thực hiện kỹ thuật phải từ 5 đến 500 ký tự.',
        });
      }
    }
  });

export type UpdateDiagnosticServiceStatusFormValues = z.infer<
  typeof updateDiagnosticServiceStatusSchema
>;
export type UpdateDiagnosticServiceStatusBatchFormValues = z.infer<
  typeof updateDiagnosticServiceStatusBatchSchema
>;
