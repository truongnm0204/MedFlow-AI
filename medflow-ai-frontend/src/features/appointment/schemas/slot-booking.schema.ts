import { z } from 'zod';

export const slotBookingSelectionSchema = z.object({
  doctorId: z.string().min(1, 'Vui lòng chọn bác sĩ thăm khám'),
  serviceCode: z.string().min(1, 'Vui lòng chọn dịch vụ khám'),
  patientProfileId: z.string().min(1, 'Vui lòng chọn hồ sơ người thăm khám'),
  selectedDateIso: z.string().min(1, 'Vui lòng chọn ngày khám'),
  selectedSlotId: z.string().min(1, 'Vui lòng chọn khung giờ thăm khám'),
  symptomNote: z
    .string()
    .max(500, 'Lý do khám / triệu chứng không vượt quá 500 ký tự')
    .optional()
    .default(''),
});

export const serviceShiftBookingSelectionSchema = z.object({
  serviceId: z.string().min(1, 'Vui lòng chọn dịch vụ khám'),
  serviceCode: z.string().min(1, 'Vui lòng chọn mã dịch vụ khám'),
  patientProfileId: z.string().min(1, 'Vui lòng chọn hồ sơ người thăm khám'),
  selectedDateIso: z.string().min(1, 'Vui lòng chọn ngày khám'),
  selectedShiftId: z.string().min(1, 'Vui lòng chọn ca khám'),
  symptomNote: z
    .string()
    .max(500, 'Lý do khám / triệu chứng không vượt quá 500 ký tự')
    .optional()
    .default(''),
});

export type SlotBookingSelectionInput = z.infer<typeof slotBookingSelectionSchema>;
export type ServiceShiftBookingSelectionInput = z.infer<typeof serviceShiftBookingSelectionSchema>;
