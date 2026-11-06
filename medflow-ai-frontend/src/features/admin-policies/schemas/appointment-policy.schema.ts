import { z } from 'zod';

import { policyIntField } from './policy-int-field';

export const appointmentPolicySchema = z
  .object({
    bookingWindowDays: policyIntField('Số ngày được đặt trước tối đa', 1, 365),
    bookingLeadHours: policyIntField('Số giờ đặt trước tối thiểu', 0, 168),
    maxActiveBookings: policyIntField('Số lịch hẹn hoạt động tối đa', 1, 20),
    rescheduleMinimumHours: policyIntField('Số giờ tối thiểu trước khi đổi lịch', 0, 168),
    maxReschedules: policyIntField('Số lần đổi lịch tối đa', 0, 10),
    cancelMinimumHours: policyIntField('Số giờ tối thiểu trước khi hủy lịch', 0, 168),
    noShowGraceMinutes: policyIntField('Số phút chờ trước khi tính vắng mặt', 0, 120),
    noShowMaxCount: policyIntField('Số lần vắng mặt tối đa', 1, 20),
    noShowLookbackDays: policyIntField('Số ngày xét vắng mặt', 1, 365),
    noShowBlockDays: policyIntField('Số ngày khóa đặt lịch', 0, 365),
  })
  .refine((data) => data.bookingLeadHours < data.bookingWindowDays * 24, {
    message: 'Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.',
    path: ['bookingLeadHours'],
  });

export type AppointmentPolicyFormValues = z.infer<typeof appointmentPolicySchema>;
