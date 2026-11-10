import { z } from 'zod';

export const prescriptionLineItemSchema = z.object({
  id: z.string().min(1),
  drugCode: z.string().min(1),
  drugName: z.string().min(1, 'Tên thuốc không được trống'),
  quantity: z.number().int().min(1, 'Số lượng thuốc phải >= 1'),
  dosageInstruction: z.string().trim().min(3, 'Vui lòng nhập liều dùng & hướng dẫn sử dụng'),
});

export const createPrescriptionSchema = z.object({
  items: z.array(prescriptionLineItemSchema).min(1, 'Đơn thuốc phải có ít nhất 1 loại thuốc'),
  doctorAdvice: z.string().trim().max(2000, 'Lời dặn bác sĩ tối đa 2000 ký tự'),
});

export type CreatePrescriptionValidationResult = z.infer<typeof createPrescriptionSchema>;
