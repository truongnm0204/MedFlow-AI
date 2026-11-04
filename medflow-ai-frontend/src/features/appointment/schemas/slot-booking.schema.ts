import { z } from 'zod';

export const slotBookingSelectionSchema = z.object({
  doctorId: z.string().min(1, 'Vui lòng chọn bác sĩ thăm khám'),
  serviceCode: z.string().min(1, 'Vui lòng chọn dịch vụ khám'),
  patientProfileId: z.string().min(1, 'Vui lòng chọn hồ sơ người thăm khám'),
  selectedDateIso: z.string().min(1, 'Vui lòng chọn ngày khám'),
  selectedSlotId: z.string().min(1, 'Vui lòng chọn khung giờ thăm khám'),
  symptomNote: z
    .string()
    .trim()
    .min(3, 'Vui lòng nhập lý do khám / triệu chứng (tối thiểu 3 ký tự)')
    .max(500, 'Lý do khám / triệu chứng không vượt quá 500 ký tự'),
});

export const serviceShiftBookingSelectionSchema = z.object({
  serviceId: z.string().min(1, 'Vui lòng chọn dịch vụ khám'),
  serviceCode: z.string().min(1, 'Vui lòng chọn mã dịch vụ khám'),
  patientProfileId: z.string().min(1, 'Vui lòng chọn hồ sơ người thăm khám'),
  selectedDateIso: z.string().min(1, 'Vui lòng chọn ngày khám'),
  selectedShiftId: z.string().min(1, 'Vui lòng chọn ca khám'),
  symptomNote: z
    .string()
    .trim()
    .min(3, 'Vui lòng nhập lý do khám / triệu chứng (tối thiểu 3 ký tự)')
    .max(500, 'Lý do khám / triệu chứng không vượt quá 500 ký tự'),
});

export type SlotBookingSelectionInput = z.infer<typeof slotBookingSelectionSchema>;
export type ServiceShiftBookingSelectionInput = z.infer<typeof serviceShiftBookingSelectionSchema>;
