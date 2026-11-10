import { z } from 'zod';

import type {
  DiagnosisConclusionFieldErrors,
  DiagnosisConclusionFormValues,
} from '../types/diagnosis-conclusion.types';

const ICD10_CODE_REGEX = /^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$/;

const icd10CodeSchema = z
  .string()
  .trim()
  .toUpperCase()
  .regex(ICD10_CODE_REGEX, 'Mã ICD-10 không hợp lệ (ví dụ: K29.5).');

const noAngleBrackets = (value: string) => !value.includes('<') && !value.includes('>');

function optionalText(label: string, max: number) {
  return z
    .string()
    .trim()
    .max(max, `${label} tối đa ${max} ký tự.`)
    .refine(noAngleBrackets, `${label} không được chứa ký tự < hoặc >.`);
}

export const diagnosisConclusionSchema = z
  .object({
    primaryIcd10Code: icd10CodeSchema,
    secondaryIcd10Codes: z.array(icd10CodeSchema).max(10, 'Tối đa 10 bệnh kèm theo.'),
    differentialNotes: optionalText('Ghi chú chẩn đoán phân biệt', 1000),
    clinicalConclusion: z
      .string()
      .trim()
      .min(10, 'Kết luận lâm sàng phải có tối thiểu 10 ký tự.')
      .max(2000, 'Kết luận lâm sàng tối đa 2000 ký tự.')
      .refine(noAngleBrackets, 'Kết luận lâm sàng không được chứa ký tự < hoặc >.'),
    lifestyleInstructions: optionalText('Dặn dò sinh hoạt', 1000),
    warningSigns: optionalText('Dấu hiệu cần đi cấp cứu', 1000),
    followUp: z.object({
      enabled: z.boolean(),
      date: z.string().trim(),
      time: z.string().trim(),
      room: z.string().trim().max(60, 'Phòng khám tối đa 60 ký tự.'),
    }),
  })
  .superRefine((value, ctx) => {
    if (value.secondaryIcd10Codes.includes(value.primaryIcd10Code)) {
      ctx.addIssue({
        code: 'custom',
        path: ['secondaryIcd10Codes'],
        message: 'Bệnh kèm theo không được trùng với bệnh chính.',
      });
    }
    if (new Set(value.secondaryIcd10Codes).size !== value.secondaryIcd10Codes.length) {
      ctx.addIssue({
        code: 'custom',
        path: ['secondaryIcd10Codes'],
        message: 'Bệnh kèm theo không được trùng lặp.',
      });
    }
    if (value.followUp.enabled) {
      if (!value.followUp.date) {
        ctx.addIssue({
          code: 'custom',
          path: ['followUp', 'date'],
          message: 'Vui lòng chọn ngày tái khám.',
        });
      }
      if (!value.followUp.time) {
        ctx.addIssue({
          code: 'custom',
          path: ['followUp', 'time'],
          message: 'Vui lòng chọn giờ tái khám.',
        });
      }
      if (!value.followUp.room) {
        ctx.addIssue({
          code: 'custom',
          path: ['followUp', 'room'],
          message: 'Vui lòng nhập phòng tái khám.',
        });
      }
    }
  });

export type DiagnosisConclusionInput = z.infer<typeof diagnosisConclusionSchema>;

const PRIMARY_REQUIRED_MESSAGE = 'Vui lòng chọn chẩn đoán bệnh chính (ICD-10).';

export function validateDiagnosisConclusion(
  values: DiagnosisConclusionFormValues,
): DiagnosisConclusionFieldErrors {
  if (!values.primaryIcd10Code.trim()) {
    const rest = diagnosisConclusionSchema.safeParse({ ...values, primaryIcd10Code: 'A00' });
    const errors = rest.success ? {} : mapIssues(rest.error.issues);
    return { ...errors, primaryIcd10Code: PRIMARY_REQUIRED_MESSAGE };
  }
  const result = diagnosisConclusionSchema.safeParse(values);
  return result.success ? {} : mapIssues(result.error.issues);
}

function mapIssues(issues: readonly z.core.$ZodIssue[]): DiagnosisConclusionFieldErrors {
  const errors: DiagnosisConclusionFieldErrors = {};
  for (const issue of issues) {
    const [head, sub] = issue.path;
    let key: keyof DiagnosisConclusionFieldErrors | null = null;
    if (head === 'followUp') {
      key =
        sub === 'date'
          ? 'followUpDate'
          : sub === 'time'
            ? 'followUpTime'
            : sub === 'room'
              ? 'followUpRoom'
              : null;
    } else if (typeof head === 'string') {
      key = head as keyof DiagnosisConclusionFieldErrors;
    }
    if (key && !errors[key]) errors[key] = issue.message;
  }
  return errors;
}
