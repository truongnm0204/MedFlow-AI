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

const amendMarkupMessage = {
  resultText: 'Nội dung kết quả cận lâm sàng chứa ký tự không hợp lệ.',
  conclusion: 'Kết luận cận lâm sàng chứa ký tự không hợp lệ.',
  correctionReason: 'Lý do sửa kết quả cận lâm sàng chứa ký tự không hợp lệ.',
} as const;

function rejectsMarkup(value: string): boolean {
  return !value.includes('<') && !value.includes('>');
}

// Schema sửa kết quả đã khóa. Khớp ValidateUpdateResultRequest (BE #136 / ADR 0035).
export const updateDiagnosticResultSchema = z
  .object({
    resultText: z
      .string()
      .trim()
      .min(3, 'Nội dung kết quả cận lâm sàng (ResultText) phải có ít nhất 3 ký tự.')
      .max(5000, 'Nội dung kết quả cận lâm sàng (ResultText) không được vượt quá 5000 ký tự.')
      .refine(rejectsMarkup, amendMarkupMessage.resultText),
    conclusion: z
      .string()
      .trim()
      .min(3, 'Kết luận cận lâm sàng (Conclusion) phải có ít nhất 3 ký tự.')
      .max(2000, 'Kết luận cận lâm sàng (Conclusion) không được vượt quá 2000 ký tự.')
      .refine(rejectsMarkup, amendMarkupMessage.conclusion),
    reportFileUrl: z
      .string()
      .max(500, 'Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) không được vượt quá 500 ký tự.')
      .optional(),
    correctionReason: z
      .string()
      .trim()
      .min(5, 'Lý do sửa kết quả cận lâm sàng (CorrectionReason) phải có ít nhất 5 ký tự.')
      .max(500, 'Lý do sửa kết quả cận lâm sàng (CorrectionReason) không được vượt quá 500 ký tự.')
      .refine(rejectsMarkup, amendMarkupMessage.correctionReason),
    expectedStateVersion: z.number().int().min(1, 'ExpectedStateVersion phải lớn hơn hoặc bằng 1.'),
  })
  .superRefine((data, ctx) => {
    const reportFileUrl = data.reportFileUrl;
    if (reportFileUrl == null || reportFileUrl.length === 0) {
      return;
    }

    if (reportFileUrl.includes('<') || reportFileUrl.includes('>') || /\s/.test(reportFileUrl)) {
      ctx.addIssue({
        code: 'custom',
        path: ['reportFileUrl'],
        message: 'Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) chứa ký tự không hợp lệ.',
      });
      return;
    }

    const hasAllowedPrefix =
      reportFileUrl.startsWith('/') ||
      reportFileUrl.toLowerCase().startsWith('http://') ||
      reportFileUrl.toLowerCase().startsWith('https://');
    if (!hasAllowedPrefix) {
      ctx.addIssue({
        code: 'custom',
        path: ['reportFileUrl'],
        message:
          "Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) phải bắt đầu bằng '/', 'http://' hoặc 'https://'.",
      });
    }
  });

export type UpdateDiagnosticResultFormValues = z.infer<typeof updateDiagnosticResultSchema>;
