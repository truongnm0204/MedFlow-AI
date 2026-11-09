import { z } from 'zod';

export const consultationIdentifierSchema = z
  .string()
  .trim()
  .min(1, 'Vui lòng nhập mã lượt khám hoặc số phiếu hàng đợi.')
  .max(40, 'Mã lượt khám tối đa 40 ký tự.')
  .regex(
    /^[A-Za-z0-9#_-]+$/,
    'Mã lượt khám chỉ được chứa chữ cái, chữ số, dấu #, gạch ngang (-) hoặc gạch dưới (_).',
  );

export const openConsultationWorkspaceSchema = z.object({
  expectedStateVersion: z
    .number()
    .int('Phiên bản trạng thái phải là số nguyên.')
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
  notes: z
    .string()
    .trim()
    .max(300, 'Ghi chú tối đa 300 ký tự.')
    .refine(
      (val) => !val.includes('<') && !val.includes('>'),
      'Ghi chú không được chứa ký tự < hoặc >.',
    )
    .nullable()
    .optional(),
});

export type ConsultationIdentifierInput = z.infer<typeof consultationIdentifierSchema>;
export type OpenConsultationWorkspaceInput = z.infer<typeof openConsultationWorkspaceSchema>;
