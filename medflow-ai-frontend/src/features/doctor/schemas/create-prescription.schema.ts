import { z } from 'zod';

import type {
  CreatePrescriptionItemRequest,
  CreatePrescriptionRequest,
  PrescriptionFulfillmentChoice,
} from '../types/consultation-workspace.types';

const noAngleBrackets = (value: string) => !value.includes('<') && !value.includes('>');

function optionalText(label: string, max: number) {
  return z
    .string()
    .trim()
    .max(max, `${label} tối đa ${max} ký tự.`)
    .refine(noAngleBrackets, `${label} không được chứa ký tự < hoặc >.`);
}

export const prescriptionLineFormSchema = z
  .object({
    medicineId: z.string().trim(),
    medicineCode: optionalText('Mã thuốc', 40),
    prescribedQuantity: z.string().trim(),
    singleDoseQuantity: z.string().trim(),
    singleDoseUnit: optionalText('Đơn vị liều', 40),
    doseScheduleText: optionalText('Lịch dùng', 500),
    frequency: optionalText('Tần suất', 160),
    durationDays: z.string().trim(),
    administrationRoute: optionalText('Đường dùng', 100),
    instructions: optionalText('Hướng dẫn', 1000),
  })
  .superRefine((line, ctx) => {
    const idText = line.medicineId.trim();
    const id = idText === '' ? null : Number(idText);
    const code = line.medicineCode.trim();
    const idValid = id !== null && Number.isInteger(id) && id > 0;

    if (!idValid && code.length === 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['medicineCode'],
        message: 'Nhập mã thuốc hoặc ID thuốc.',
      });
    }
    if (idText !== '' && !idValid) {
      ctx.addIssue({
        code: 'custom',
        path: ['medicineId'],
        message: 'ID thuốc phải là số nguyên dương.',
      });
    }

    const quantity = Number(line.prescribedQuantity);
    if (!Number.isFinite(quantity) || quantity <= 0 || quantity > 9999) {
      ctx.addIssue({
        code: 'custom',
        path: ['prescribedQuantity'],
        message: 'Số lượng phải lớn hơn 0 và không quá 9999.',
      });
    }

    if (line.singleDoseQuantity.trim() !== '') {
      const dose = Number(line.singleDoseQuantity);
      if (!Number.isFinite(dose) || dose <= 0 || dose > 9999) {
        ctx.addIssue({
          code: 'custom',
          path: ['singleDoseQuantity'],
          message: 'Liều một lần phải lớn hơn 0 và không quá 9999.',
        });
      }
    }

    if (line.durationDays.trim() !== '') {
      const days = Number(line.durationDays);
      if (!Number.isInteger(days) || days < 1 || days > 365) {
        ctx.addIssue({
          code: 'custom',
          path: ['durationDays'],
          message: 'Số ngày dùng phải từ 1 đến 365.',
        });
      }
    }

    if (line.doseScheduleText.trim().length === 0 && line.instructions.trim().length === 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['doseScheduleText'],
        message: 'Nhập lịch dùng hoặc hướng dẫn sử dụng.',
      });
    }
  });

export const createPrescriptionFormSchema = z
  .object({
    fulfillmentChoice: z.enum(['HOSPITAL', 'HOSPITAL_PHARMACY', 'EXTERNAL_PHARMACY']),
    items: z
      .array(prescriptionLineFormSchema)
      .min(1, 'Đơn thuốc phải có ít nhất 1 dòng.')
      .max(20, 'Đơn thuốc tối đa 20 dòng.'),
  })
  .superRefine((form, ctx) => {
    const codes = new Set<string>();
    const ids = new Set<string>();
    form.items.forEach((item, index) => {
      const code = item.medicineCode.trim().toUpperCase();
      if (code.length > 0) {
        if (codes.has(code)) {
          ctx.addIssue({
            code: 'custom',
            path: ['items', index, 'medicineCode'],
            message: 'Mã thuốc bị trùng trong đơn.',
          });
        }
        codes.add(code);
      }
      const id = item.medicineId.trim();
      if (id.length > 0) {
        if (ids.has(id)) {
          ctx.addIssue({
            code: 'custom',
            path: ['items', index, 'medicineId'],
            message: 'ID thuốc bị trùng trong đơn.',
          });
        }
        ids.add(id);
      }
    });
  });

export type PrescriptionLineFormValues = z.infer<typeof prescriptionLineFormSchema>;
export type CreatePrescriptionFormValues = z.infer<typeof createPrescriptionFormSchema>;

function emptyToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function toCreatePrescriptionRequest(
  form: CreatePrescriptionFormValues,
  versions: { expectedStateVersion?: number | null; expectedRevisionNo?: number | null },
): CreatePrescriptionRequest {
  const items: CreatePrescriptionItemRequest[] = form.items.map((line) => {
    const medicineId = line.medicineId.trim() === '' ? null : Number(line.medicineId);
    const item: CreatePrescriptionItemRequest = {
      prescribedQuantity: Number(line.prescribedQuantity),
    };
    if (medicineId !== null) item.medicineId = medicineId;
    const medicineCode = emptyToNull(line.medicineCode);
    if (medicineCode) item.medicineCode = medicineCode.toUpperCase();
    if (line.singleDoseQuantity.trim() !== '') {
      item.singleDoseQuantity = Number(line.singleDoseQuantity);
    }
    const singleDoseUnit = emptyToNull(line.singleDoseUnit);
    if (singleDoseUnit) item.singleDoseUnit = singleDoseUnit;
    const doseScheduleText = emptyToNull(line.doseScheduleText);
    if (doseScheduleText) item.doseScheduleText = doseScheduleText;
    const frequency = emptyToNull(line.frequency);
    if (frequency) item.frequency = frequency;
    if (line.durationDays.trim() !== '') item.durationDays = Number(line.durationDays);
    const administrationRoute = emptyToNull(line.administrationRoute);
    if (administrationRoute) item.administrationRoute = administrationRoute;
    const instructions = emptyToNull(line.instructions);
    if (instructions) item.instructions = instructions;
    return item;
  });

  return {
    items,
    fulfillmentChoice: form.fulfillmentChoice as PrescriptionFulfillmentChoice,
    expectedStateVersion: versions.expectedStateVersion ?? null,
    expectedRevisionNo: versions.expectedRevisionNo ?? null,
  };
}

export function blankPrescriptionLine(): PrescriptionLineFormValues {
  return {
    medicineId: '',
    medicineCode: '',
    prescribedQuantity: '1',
    singleDoseQuantity: '',
    singleDoseUnit: '',
    doseScheduleText: '',
    frequency: '',
    durationDays: '',
    administrationRoute: '',
    instructions: '',
  };
}
