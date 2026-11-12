import { z } from 'zod';

import type {
  DiagnosisConclusionFieldErrors,
  DiagnosisConclusionFormValues,
  DiagnosisSaveMode,
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

const CONCLUSION_MIN = 'Kết luận lâm sàng phải có tối thiểu 10 ký tự.';
const CONCLUSION_MAX = 'Kết luận lâm sàng tối đa 2000 ký tự.';
const CONCLUSION_BRACKETS = 'Kết luận lâm sàng không được chứa ký tự < hoặc >.';

function buildDiagnosisConclusionSchema(mode: DiagnosisSaveMode) {
  const clinicalConclusion =
    mode === 'confirm'
      ? z
          .string()
          .trim()
          .min(10, CONCLUSION_MIN)
          .max(2000, CONCLUSION_MAX)
          .refine(noAngleBrackets, CONCLUSION_BRACKETS)
      : z
          .string()
          .trim()
          .max(2000, CONCLUSION_MAX)
          .refine(noAngleBrackets, CONCLUSION_BRACKETS)
          .refine((value) => value.length === 0 || value.length >= 10, CONCLUSION_MIN);

  return z
    .object({
      primaryIcd10Code: icd10CodeSchema,
      secondaryIcd10Codes: z.array(icd10CodeSchema).max(10, 'Tối đa 10 bệnh kèm theo.'),
      differentialNotes: optionalText('Ghi chú chẩn đoán phân biệt', 1000),
      clinicalConclusion,
      lifestyleInstructions: optionalText('Dặn dò sinh hoạt', 1000),
      warningSigns: optionalText('Dấu hiệu cần đi cấp cứu', 1000),
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
    });
}

export interface DiagnosisConclusionInput {
  primaryIcd10Code: string;
  secondaryIcd10Codes: string[];
  differentialNotes: string;
  clinicalConclusion: string;
  lifestyleInstructions: string;
  warningSigns: string;
}

const PRIMARY_REQUIRED_MESSAGE = 'Vui lòng chọn chẩn đoán bệnh chính (ICD-10).';

export function validateDiagnosisConclusion(
  values: DiagnosisConclusionFormValues,
  mode: DiagnosisSaveMode = 'confirm',
): DiagnosisConclusionFieldErrors {
  const schema = buildDiagnosisConclusionSchema(mode);
  if (!values.primaryIcd10Code.trim()) {
    const rest = schema.safeParse({ ...values, primaryIcd10Code: 'A00' });
    const errors = rest.success ? {} : mapIssues(rest.error.issues);
    return { ...errors, primaryIcd10Code: PRIMARY_REQUIRED_MESSAGE };
  }
  const result = schema.safeParse(values);
  return result.success ? {} : mapIssues(result.error.issues);
}

function mapIssues(issues: readonly z.core.$ZodIssue[]): DiagnosisConclusionFieldErrors {
  const errors: DiagnosisConclusionFieldErrors = {};
  for (const issue of issues) {
    const head = issue.path[0];
    if (typeof head !== 'string') continue;
    const key = head as keyof DiagnosisConclusionFieldErrors;
    if (!errors[key]) errors[key] = issue.message;
  }
  return errors;
}
