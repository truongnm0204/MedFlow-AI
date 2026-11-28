import { z } from 'zod';

import {
  PATIENT_UNVERIFIED_REASONS,
  PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION,
} from '@/features/ekyc/types/ekyc.types';
import type {
  CreateUnverifiedPatientProfileRequest,
  PatientUnverifiedReason,
} from '@/features/ekyc/types/ekyc.types';

const PHONE_REGEX = /^0[35789]\d{8}$/;

const REASON_VALUES = PATIENT_UNVERIFIED_REASONS as [
  PatientUnverifiedReason,
  ...PatientUnverifiedReason[],
];

const REASON_REQUIRING_ID_MESSAGE = 'Lý do này bắt buộc nhập số CCCD/VNeID';

function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * Hồ sơ tạo không qua eKYC: chỉ họ tên và lý do là bắt buộc.
 * Riêng các lý do do backend quy định (CONSENT_DECLINED, VNPT_UNAVAILABLE, QUOTA_EXHAUSTED)
 * bắt buộc khai báo số giấy tờ định danh để phục vụ đối chiếu sau này.
 */
export const createUnverifiedPatientSchema = z
  .object({
    fullName: z.string().trim().min(2, 'Họ và tên tối thiểu 2 ký tự'),
    dateOfBirth: z
      .string()
      .refine((v) => v === '' || v <= todayIso(), 'Ngày sinh không được lớn hơn ngày hiện tại'),
    gender: z.union([z.enum(['MALE', 'FEMALE', 'OTHER']), z.literal('')]),
    phone: z
      .string()
      .trim()
      .refine((v) => v === '' || PHONE_REGEX.test(v), 'Số điện thoại không hợp lệ'),
    email: z
      .string()
      .trim()
      .refine((v) => v === '' || z.email().safeParse(v).success, 'Email không hợp lệ'),
    address: z.string().trim(),
    identityCard: z
      .string()
      .trim()
      .refine(
        (v) => v === '' || /^(?:\d{9}|\d{12})$/.test(v),
        'CCCD/VNeID phải gồm đúng 9 hoặc 12 chữ số',
      ),
    reason: z.enum(REASON_VALUES, { error: 'Vui lòng chọn lý do tạo hồ sơ không xác minh' }),
    note: z.string().trim(),
  })
  .superRefine((data, ctx) => {
    if (
      PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION.includes(data.reason) &&
      data.identityCard === ''
    ) {
      ctx.addIssue({
        code: 'custom',
        path: ['identityCard'],
        message: REASON_REQUIRING_ID_MESSAGE,
      });
    }
  });

export type CreateUnverifiedPatientSchemaOutput = z.infer<typeof createUnverifiedPatientSchema>;

/** Chuyển dữ liệu form (đã qua zod) sang payload API, bỏ các trường rỗng. */
export function toCreateUnverifiedPatientPayload(
  data: CreateUnverifiedPatientSchemaOutput,
): CreateUnverifiedPatientProfileRequest {
  return {
    fullName: data.fullName,
    dateOfBirth: data.dateOfBirth || undefined,
    gender: data.gender || undefined,
    phone: data.phone || undefined,
    address: data.address || undefined,
    email: data.email || undefined,
    identityCard: data.identityCard || undefined,
    reason: data.reason,
    note: data.note || undefined,
  };
}
