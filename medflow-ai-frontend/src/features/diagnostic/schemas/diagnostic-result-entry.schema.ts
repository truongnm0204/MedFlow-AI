import { z } from 'zod';

// Schema validate biểu mẫu nhập kết quả cận lâm sàng (UC43 — M3-133).
// Đồng bộ với ràng buộc nghiệp vụ của POST /diagnostic/orders/{idOrCode}/result (BE #134 + Phase 06).

export const diagnosticResultDraftStatusSchema = z.enum(
  ['DRAFT', 'PRELIMINARY', 'PENDING_VERIFY', 'FINALIZED', 'VERIFIED'],
  'Trạng thái lưu kết quả không hợp lệ.',
);

const amendMarkupMessage = {
  resultText: 'Nội dung kết quả cận lâm sàng chứa ký tự không hợp lệ.',
  conclusion: 'Kết luận cận lâm sàng chứa ký tự không hợp lệ.',
  correctionReason: 'Lý do sửa kết quả cận lâm sàng chứa ký tự không hợp lệ.',
} as const;

function rejectsMarkup(value: string): boolean {
  return !value.includes('<') && !value.includes('>');
}

export function isValidInternalDiagnosticReportUrl(rawUrl: string): boolean {
  const trimmed = rawUrl.trim();
  if (!trimmed) {
    return true;
  }

  if (
    trimmed.length > 500 ||
    trimmed.includes('<') ||
    trimmed.includes('>') ||
    /\s/.test(trimmed) ||
    trimmed.includes('\\') ||
    trimmed.includes('..') ||
    trimmed.includes('//') ||
    trimmed.includes('://')
  ) {
    return false;
  }

  const lower = trimmed.toLowerCase();
  return (
    lower.startsWith('/storage/diagnostic-reports/') ||
    lower.startsWith('/api/v1/diagnostic/results/')
  );
}

const INTERNAL_REPORT_URL_ERROR =
  'Đường dẫn tệp báo cáo chỉ chấp nhận đường dẫn nội bộ được bảo vệ (/storage/diagnostic-reports/... hoặc /api/v1/diagnostic/results/...), không chấp nhận URL ngoài.';

export const enterDiagnosticResultSchema = z
  .object({
    resultText: z
      .string({ message: 'Vui lòng nhập nội dung kết quả cận lâm sàng.' })
      .trim()
      .min(3, 'Nội dung kết quả cận lâm sàng (ResultText) phải có ít nhất 3 ký tự.')
      .max(5000, 'Nội dung kết quả cận lâm sàng (ResultText) không vượt quá 5000 ký tự.')
      .refine(rejectsMarkup, amendMarkupMessage.resultText),
    conclusion: z
      .string()
      .trim()
      .max(500, 'Kết luận không vượt quá 500 ký tự.')
      .refine(rejectsMarkup, amendMarkupMessage.conclusion)
      .nullable()
      .optional(),
    reportFileUrl: z
      .string()
      .trim()
      .max(500, 'Đường dẫn tệp kết quả không vượt quá 500 ký tự.')
      .nullable()
      .optional()
      .transform((val) => (val && val.trim().length > 0 ? val.trim() : null)),
    status: diagnosticResultDraftStatusSchema,
    isFinalized: z.boolean(),
    isVerified: z.boolean().optional(),
    correctionReason: z
      .string()
      .trim()
      .max(500, 'Lý do chỉnh sửa không vượt quá 500 ký tự.')
      .nullable()
      .optional(),
    notes: z.string().trim().max(500, 'Ghi chú không vượt quá 500 ký tự.').nullable().optional(),
    expectedStateVersion: z
      .number()
      .int()
      .min(0, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 0.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    const trimmedConclusion = data.conclusion?.trim() ?? '';
    if (data.isFinalized) {
      if (!trimmedConclusion) {
        ctx.addIssue({
          code: 'custom',
          path: ['conclusion'],
          message: 'Vui lòng nhập kết luận trước khi duyệt & trả kết quả.',
        });
      } else if (trimmedConclusion.length < 3) {
        ctx.addIssue({
          code: 'custom',
          path: ['conclusion'],
          message: 'Kết luận cận lâm sàng phải có ít nhất 3 ký tự.',
        });
      }
    } else if (trimmedConclusion.length > 0 && trimmedConclusion.length < 3) {
      ctx.addIssue({
        code: 'custom',
        path: ['conclusion'],
        message: 'Kết luận cận lâm sàng (nếu nhập) phải có ít nhất 3 ký tự.',
      });
    }

    if (data.reportFileUrl && !isValidInternalDiagnosticReportUrl(data.reportFileUrl)) {
      ctx.addIssue({
        code: 'custom',
        path: ['reportFileUrl'],
        message: INTERNAL_REPORT_URL_ERROR,
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

// Schema sửa kết quả đã khóa. Khớp ValidateUpdateResultRequest (BE #136 / ADR 0035 + Phase 06).
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
      .max(500, 'Kết luận cận lâm sàng (Conclusion) không được vượt quá 500 ký tự.')
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

    if (!isValidInternalDiagnosticReportUrl(reportFileUrl)) {
      ctx.addIssue({
        code: 'custom',
        path: ['reportFileUrl'],
        message: INTERNAL_REPORT_URL_ERROR,
      });
    }
  });

export type UpdateDiagnosticResultFormValues = z.infer<typeof updateDiagnosticResultSchema>;
