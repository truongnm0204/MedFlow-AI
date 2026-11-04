import { z } from 'zod';

const PHONE_REGEX = /^0[35789]\d{8}$/;

export const updatePatientSchema = z.object({
  fullName: z.string().trim().min(2, 'Họ và tên tối thiểu 2 ký tự'),
  dateOfBirth: z
    .string()
    .min(1, 'Vui lòng chọn ngày sinh')
    .refine((v) => !Number.isNaN(Date.parse(v)) && new Date(v) <= new Date(), {
      message: 'Ngày sinh không hợp lệ',
    }),
  gender: z.enum(['MALE', 'FEMALE', 'OTHER'], { message: 'Vui lòng chọn giới tính' }),
  ethnicity: z.string().trim().min(1, 'Vui lòng nhập dân tộc'),
  nationalId: z.string().regex(/^\d{12}$/, 'Số CCCD/VNeID phải gồm 12 chữ số'),
  phone: z.string().regex(PHONE_REGEX, 'Số điện thoại phải gồm 10 chữ số, bắt đầu 03/05/07/08/09'),
  email: z.union([z.literal(''), z.email('Email không hợp lệ')]),
  address: z.string().trim().min(1, 'Vui lòng nhập địa chỉ thường trú'),
  insuranceNumber: z.union([z.literal(''), z.string().length(15, 'Số thẻ BHYT gồm 15 ký tự')]),
  insuranceFacility: z.string().trim(),
  emergencyContactName: z.string().trim(),
  emergencyContactRelationship: z.string().trim(),
  emergencyContactPhone: z.union([
    z.literal(''),
    z.string().regex(PHONE_REGEX, 'Số điện thoại phải gồm 10 chữ số, bắt đầu 03/05/07/08/09'),
  ]),
});

export type UpdatePatientSchemaOutput = z.infer<typeof updatePatientSchema>;
