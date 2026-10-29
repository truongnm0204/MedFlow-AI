import { z } from 'zod';

export const quickBookingSchema = z.object({
  specialtyId: z.string().min(1, 'Vui lòng chọn chuyên khoa khám.'),
  doctorId: z.string().min(1, 'Vui lòng chọn bác sĩ hoặc chế độ phân bổ tự động.'),
  dateOption: z.enum(['today', 'tomorrow', 'weekend'], {
    message: 'Vui lòng chọn ngày khám hợp lệ.',
  }),
  symptomNote: z.string().max(200, 'Ghi chú triệu chứng không được vượt quá 200 ký tự.').optional(),
});

export type QuickBookingFormValues = z.infer<typeof quickBookingSchema>;

export const aiTriageSchema = z.object({
  symptomInput: z
    .string()
    .trim()
    .min(3, 'Vui lòng nhập mô tả triệu chứng tối thiểu 3 ký tự để AI phân luồng.')
    .max(250, 'Mô tả triệu chứng không được vượt quá 250 ký tự.'),
});

export type AiTriageFormValues = z.infer<typeof aiTriageSchema>;
