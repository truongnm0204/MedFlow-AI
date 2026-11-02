import { z } from 'zod';

export const rescheduleAppointmentSchema = z.object({
  targetSlotId: z.string().min(1, 'Vui lòng chọn khung giờ mới đề xuất để đổi lịch.'),
  rescheduleNote: z
    .string()
    .trim()
    .max(250, 'Ghi chú đổi lịch không được vượt quá 250 ký tự.')
    .optional()
    .or(z.literal('')),
});

export type RescheduleAppointmentFormValues = z.infer<typeof rescheduleAppointmentSchema>;

export const cancelAppointmentSchema = z.object({
  cancellationReason: z
    .string()
    .trim()
    .min(10, 'Vui lòng nhập lý do hủy lịch tối thiểu 10 ký tự.')
    .max(250, 'Lý do hủy lịch không được vượt quá 250 ký tự.'),
});

export type CancelAppointmentFormValues = z.infer<typeof cancelAppointmentSchema>;

export const updateReasonNoteSchema = z.object({
  reasonForVisit: z
    .string()
    .trim()
    .min(10, 'Lý do khám / triệu chứng ban đầu cần tối thiểu 10 ký tự.')
    .max(300, 'Lý do khám / triệu chứng ban đầu không được vượt quá 300 ký tự.'),
});

export type UpdateReasonNoteFormValues = z.infer<typeof updateReasonNoteSchema>;
