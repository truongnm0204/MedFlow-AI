import { z } from 'zod';

export const reviewBookingModeSchema = z.enum(['doctor', 'service']);

export const reviewBookingQuerySchema = z.object({
  mode: reviewBookingModeSchema.optional().default('doctor'),
  doctorId: z.string().trim().optional(),
  serviceId: z.string().trim().optional(),
  serviceCode: z.string().trim().optional(),
  patientId: z.string().trim().optional(),
  date: z.string().trim().optional(),
  slotId: z.string().trim().optional(),
  shiftId: z.string().trim().optional(),
});

export const confirmBookingSchema = z.object({
  mode: reviewBookingModeSchema,
  patientId: z.string().trim().min(1, 'Vui lòng chọn hồ sơ người bệnh thăm khám.'),
  doctorId: z.string().trim().min(1, 'Thông tin bác sĩ phụ trách không hợp lệ.'),
  serviceId: z.string().trim().min(1, 'Thông tin dịch vụ kỹ thuật y tế không hợp lệ.'),
  serviceCode: z.string().trim().min(1, 'Mã dịch vụ khám không hợp lệ.'),
  dateIso: z
    .string()
    .trim()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Ngày khám không đúng định dạng chuẩn (YYYY-MM-DD).'),
  slotOrShiftId: z.string().trim().min(1, 'Vui lòng chọn khung giờ hoặc ca khám hợp lệ.'),
  symptomNote: z
    .string()
    .trim()
    .min(3, 'Vui lòng mô tả lý do khám tối thiểu 3 ký tự.')
    .max(500, 'Lý do khám / triệu chứng khai báo không được vượt quá 500 ký tự.'),
});

export const reviewSymptomNoteSchema = z.object({
  symptomNote: z
    .string()
    .trim()
    .min(3, 'Vui lòng mô tả lý do khám tối thiểu 3 ký tự.')
    .max(500, 'Lý do khám / triệu chứng khai báo không được vượt quá 500 ký tự.'),
});

export type ReviewBookingQueryInput = z.infer<typeof reviewBookingQuerySchema>;
export type ConfirmBookingInput = z.infer<typeof confirmBookingSchema>;
export type ReviewSymptomNoteInput = z.infer<typeof reviewSymptomNoteSchema>;
