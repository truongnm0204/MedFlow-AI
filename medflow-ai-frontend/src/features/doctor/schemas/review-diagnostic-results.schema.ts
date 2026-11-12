import { z } from 'zod';

const noAngleBrackets = (value: string) => !value.includes('<') && !value.includes('>');

export const reviewDiagnosticResultsSchema = z.object({
  orderIds: z
    .array(z.number().int().positive())
    .min(1, 'Chưa có kết quả cận lâm sàng nào sẵn sàng để xác nhận.')
    .max(20, 'Tối đa 20 phiếu chỉ định cận lâm sàng trong một lần xác nhận xem xét.'),
  clinicalInterpretation: z
    .string()
    .trim()
    .max(2000, 'Nhận định kết quả cận lâm sàng tối đa 2000 ký tự.')
    .refine(noAngleBrackets, 'Nhận định kết quả cận lâm sàng không được chứa ký tự < hoặc >.'),
});

export type ReviewDiagnosticResultsInput = z.infer<typeof reviewDiagnosticResultsSchema>;
