import { z } from 'zod';

export const checkInSchema = z.object({
  reasonForVisit: z
    .string()
    .min(3, 'Lý do khám sơ bộ phải có ít nhất 3 ký tự')
    .max(300, 'Lý do khám không được vượt quá 300 ký tự'),
  bloodPressure: z
    .string()
    .regex(/^(\d{2,3}\/\d{2,3})?$/, 'Huyết áp phải có định dạng chuẩn (ví dụ: 120/80)')
    .optional(),
  heartRate: z
    .number()
    .min(40, 'Mạch không hợp lệ (< 40 bpm)')
    .max(220, 'Mạch không hợp lệ (> 220 bpm)')
    .optional(),
  temperature: z
    .number()
    .min(34.0, 'Thân nhiệt không hợp lệ (< 34°C)')
    .max(43.0, 'Thân nhiệt không hợp lệ (> 43°C)')
    .optional(),
  isIdentityVerified: z.literal(true, {
    error: 'Bắt buộc xác nhận đã đối chiếu giấy tờ tùy thân của bệnh nhân',
  }),
  isInsuranceVerified: z.boolean().default(false),
  priority: z.enum(['NORMAL', 'URGENT', 'ELDERLY', 'PREGNANT']).default('NORMAL'),
});

export type CheckInInput = z.infer<typeof checkInSchema>;
