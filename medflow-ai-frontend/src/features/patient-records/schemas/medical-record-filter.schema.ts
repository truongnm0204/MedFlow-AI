import { z } from 'zod';

const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/;

export const medicalRecordFilterSchema = z
  .object({
    patientId: z.string(),
    fromDate: z.string().refine((val) => val === '' || isoDatePattern.test(val), {
      message: 'Ngày bắt đầu phải đúng định dạng YYYY-MM-DD.',
    }),
    toDate: z.string().refine((val) => val === '' || isoDatePattern.test(val), {
      message: 'Ngày kết thúc phải đúng định dạng YYYY-MM-DD.',
    }),
    hospitalId: z.string(),
    specialtyCode: z.string(),
    keyword: z
      .string()
      .max(100, 'Từ khóa tìm kiếm tối đa 100 ký tự.')
      .refine((val) => !val.includes('<') && !val.includes('>'), {
        message: 'Từ khóa tìm kiếm chứa ký tự không hợp lệ.',
      }),
  })
  .refine(
    (data) => {
      if (!data.fromDate || !data.toDate) return true;
      return data.fromDate <= data.toDate;
    },
    {
      message: 'Ngày bắt đầu không được lớn hơn ngày kết thúc.',
      path: ['toDate'],
    },
  );

export type MedicalRecordFilterFormValues = z.infer<typeof medicalRecordFilterSchema>;
