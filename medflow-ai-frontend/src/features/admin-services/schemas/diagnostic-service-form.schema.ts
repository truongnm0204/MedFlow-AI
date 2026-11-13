import { z } from 'zod';

import type {
  CreateDiagnosticServicePayload,
  UpdateDiagnosticServicePayload,
} from '../types/diagnostic-service.types';

const priceRangeMessage = 'Đơn giá phải lớn hơn 0 và không quá 1.000.000.000 đ.';
const priceScaleMessage = 'Đơn giá tối đa 2 chữ số thập phân.';
const deactivateReasonMessage = 'Lý do ngừng hoạt động phải từ 5 đến 500 ký tự.';

export const diagnosticServiceFormSchema = z.object({
  serviceCode: z
    .string()
    .trim()
    .superRefine((value, ctx) => {
      const code = value.toUpperCase();
      if (code.length === 0) {
        ctx.addIssue({ code: 'custom', message: 'Mã dịch vụ là bắt buộc.' });
        return;
      }
      if (code.length > 40) {
        ctx.addIssue({ code: 'custom', message: 'Mã dịch vụ tối đa 40 ký tự.' });
        return;
      }
      if (!/^[A-Z0-9][A-Z0-9_.-]*$/.test(code)) {
        ctx.addIssue({
          code: 'custom',
          message: 'Mã dịch vụ chỉ gồm chữ in hoa, số và các ký tự - _ .',
        });
      }
    })
    .transform((value) => value.toUpperCase()),
  name: z
    .string()
    .trim()
    .min(1, 'Tên dịch vụ là bắt buộc.')
    .max(200, 'Tên dịch vụ tối đa 200 ký tự.'),
  description: z
    .string()
    .trim()
    .max(2000, 'Mô tả dịch vụ tối đa 2000 ký tự.')
    .transform((value) => (value.length === 0 ? null : value)),
  departmentId: z
    .string()
    .trim()
    .refine((value) => /^\d+$/.test(value) && Number(value) > 0, 'Khoa thực hiện là bắt buộc.')
    .transform((value) => Number(value)),
  specialtyId: z
    .string()
    .trim()
    .refine(
      (value) => value.length === 0 || (/^\d+$/.test(value) && Number(value) > 0),
      'Chuyên khoa không hợp lệ.',
    )
    .transform((value) => (value.length === 0 ? null : Number(value))),
  requiresResultVerification: z.boolean(),
  price: z
    .string()
    .trim()
    .superRefine((value, ctx) => {
      if (value.length === 0) return;
      if (!/^\d+(\.\d+)?$/.test(value)) {
        ctx.addIssue({ code: 'custom', message: priceRangeMessage });
        return;
      }
      const amount = Number(value);
      if (!(amount > 0) || amount > 1_000_000_000) {
        ctx.addIssue({ code: 'custom', message: priceRangeMessage });
        return;
      }
      const decimals = value.split('.')[1];
      if (decimals && decimals.length > 2) {
        ctx.addIssue({ code: 'custom', message: priceScaleMessage });
      }
    })
    .transform((value) => (value.length === 0 ? null : Number(value))),
});

export type DiagnosticServiceFormParsed = z.output<typeof diagnosticServiceFormSchema>;

export function toCreateDiagnosticServicePayload(
  values: DiagnosticServiceFormParsed,
): CreateDiagnosticServicePayload {
  return {
    serviceCode: values.serviceCode,
    name: values.name,
    description: values.description,
    departmentId: values.departmentId,
    specialtyId: values.specialtyId,
    requiresResultVerification: values.requiresResultVerification,
    price: values.price,
  };
}

export function toUpdateDiagnosticServicePayload(
  values: DiagnosticServiceFormParsed,
): UpdateDiagnosticServicePayload {
  return {
    name: values.name,
    description: values.description,
    departmentId: values.departmentId,
    specialtyId: values.specialtyId,
    requiresResultVerification: values.requiresResultVerification,
  };
}

export const deactivateDiagnosticServiceSchema = z.object({
  reason: z.string().trim().min(5, deactivateReasonMessage).max(500, deactivateReasonMessage),
});
