import { z } from 'zod';

const PHONE_REGEX = /^0[35789]\d{8}$/;

const optionalText = z.string().trim();

function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export const createPatientSchema = z.object({
  identityCard: z
    .string()
    .trim()
    .refine((v) => v === '' || /^\d{12}$/.test(v), 'CCCD/VNeID phải gồm đúng 12 chữ số'),
  fullName: z.string().trim().min(2, 'Họ và tên tối thiểu 2 ký tự'),
  dateOfBirth: z
    .string()
    .min(1, 'Vui lòng nhập ngày sinh')
    .refine((v) => v <= todayIso(), 'Ngày sinh không được lớn hơn ngày hiện tại'),
  gender: z.enum(['MALE', 'FEMALE', 'OTHER']),
  ethnicity: optionalText.default('Kinh'),
  phone: z
    .string()
    .trim()
    .regex(PHONE_REGEX, 'Số điện thoại phải gồm 10 chữ số (đầu 03, 05, 07, 08, 09)'),
  email: z
    .string()
    .trim()
    .refine((v) => v === '' || z.email().safeParse(v).success, 'Email không hợp lệ'),
  address: z.string().trim().min(1, 'Vui lòng nhập địa chỉ thường trú'),
  insuranceNumber: z
    .string()
    .trim()
    .refine((v) => v === '' || v.length === 15, 'Mã số thẻ BHYT phải gồm 15 ký tự'),
  insuranceRegisteredPlace: optionalText,
  emergencyContactName: optionalText,
  emergencyContactRelationship: optionalText,
  emergencyContactPhone: z
    .string()
    .trim()
    .refine((v) => v === '' || PHONE_REGEX.test(v), 'Số điện thoại không hợp lệ'),
});

export type CreatePatientSchemaOutput = z.infer<typeof createPatientSchema>;
