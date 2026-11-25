import { z } from 'zod';

export const walkInVisitSchema = z
  .object({
    patientId: z.string().min(1, 'Bắt buộc chọn bệnh nhân'),
    clinicalPriority: z.enum(['NORMAL', 'ELDERLY', 'PREGNANT']),
    priorityVerificationConfirmed: z.boolean().optional(),
    departmentId: z.string().min(1, 'Vui lòng chọn khoa tiếp nhận'),
    serviceId: z.string().min(1, 'Vui lòng chọn dịch vụ khám yêu cầu'),
    reasonForVisit: z
      .string()
      .min(5, 'Lý do đến khám phải có ít nhất 5 ký tự')
      .max(500, 'Lý do đến khám không được vượt quá 500 ký tự'),
    symptoms: z.array(z.string()).default([]),
    paymentMethod: z.enum(['VIETQR', 'CASHIER']),
  })
  .superRefine((data, ctx) => {
    if (
      (data.clinicalPriority === 'ELDERLY' || data.clinicalPriority === 'PREGNANT') &&
      data.priorityVerificationConfirmed !== true
    ) {
      ctx.addIssue({
        code: 'custom',
        path: ['priorityVerificationConfirmed'],
        message: 'Bắt buộc xác nhận giấy tờ đối tượng ưu tiên (ELDERLY/PREGNANT)',
      });
    }
  });

export type WalkInVisitFormValues = z.infer<typeof walkInVisitSchema>;
