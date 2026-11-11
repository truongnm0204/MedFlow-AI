import { z } from 'zod';

// Schema validate biểu mẫu nhập kết quả cận lâm sàng (UC43 — M3-133).
// Đồng bộ với ràng buộc nghiệp vụ của POST /diagnostic/orders/{idOrCode}/result (BE #134).

export const diagnosticResultDraftStatusSchema = z.enum(
  ['DRAFT', 'PRELIMINARY', 'PENDING_VERIFY'],
  'Trạng thái lưu kết quả không hợp lệ. Chỉ chấp nhận DRAFT, PRELIMINARY hoặc PENDING_VERIFY.',
);

export const enterDiagnosticResultSchema = z
  .object({
    resultText: z
      .string({ message: 'Vui lòng nhập nội dung kết quả cận lâm sàng.' })
      .trim()
      .min(1, 'Vui lòng nhập nội dung kết quả cận lâm sàng.')
      .max(8000, 'Kết quả không vượt quá 8000 ký tự.'),
    conclusion: z
      .string()
      .trim()
      .max(2000, 'Kết luận không vượt quá 2000 ký tự.')
      .nullable()
      .optional(),
    reportFileUrl: z
      .string()
      .trim()
      .url('Đường dẫn tệp kết quả không hợp lệ.')
      .max(500, 'Đường dẫn tệp kết quả không vượt quá 500 ký tự.')
      .nullable()
      .optional()
      .or(z.literal('').transform(() => null)),
    status: diagnosticResultDraftStatusSchema,
    isFinalized: z.boolean(),
    isVerified: z.boolean().optional(),
    correctionReason: z
      .string()
      .trim()
      .max(1000, 'Lý do chỉnh sửa không vượt quá 1000 ký tự.')
      .nullable()
      .optional(),
    notes: z.string().trim().max(2000, 'Ghi chú không vượt quá 2000 ký tự.').nullable().optional(),
    expectedStateVersion: z
      .number()
      .int()
      .min(0, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 0.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    if (data.isFinalized && !data.conclusion?.trim()) {
      ctx.addIssue({
        code: 'custom',
        path: ['conclusion'],
        message: 'Vui lòng nhập kết luận trước khi duyệt & trả kết quả.',
      });
    }
    const correction = data.correctionReason?.trim();
    if (correction && correction.length < 10) {
      ctx.addIssue({
        code: 'custom',
        path: ['correctionReason'],
        message: 'Lý do chỉnh sửa cần tối thiểu 10 ký tự.',
      });
    }
  });

export type EnterDiagnosticResultFormValues = z.infer<typeof enterDiagnosticResultSchema>;
