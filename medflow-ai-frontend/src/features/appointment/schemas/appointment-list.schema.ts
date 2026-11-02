import { z } from 'zod';

import type { AppointmentListFilterParams } from '../types/appointment-list.types';

const ISO_DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

export const appointmentTabGroupSchema = z.enum(['ALL', 'UPCOMING', 'COMPLETED', 'CANCELLED']);

export const appointmentListFilterSchema = z
  .object({
    memberId: z.string().trim().default('ALL'),
    tab: appointmentTabGroupSchema.default('ALL'),
    keyword: z
      .string()
      .trim()
      .max(100, 'Từ khóa tìm kiếm không được vượt quá 100 ký tự.')
      .optional()
      .default(''),
    dateFrom: z
      .string()
      .trim()
      .optional()
      .default('')
      .refine((val) => !val || ISO_DATE_REGEX.test(val), {
        message: 'Định dạng từ ngày không hợp lệ (YYYY-MM-DD).',
      }),
    dateTo: z
      .string()
      .trim()
      .optional()
      .default('')
      .refine((val) => !val || ISO_DATE_REGEX.test(val), {
        message: 'Định dạng đến ngày không hợp lệ (YYYY-MM-DD).',
      }),
    page: z.coerce.number().int().positive().default(1),
    pageSize: z.coerce.number().int().positive().max(50).default(10),
  })
  .refine((data) => !data.dateFrom || !data.dateTo || data.dateFrom <= data.dateTo, {
    message: 'Ngày bắt đầu không được lớn hơn ngày kết thúc.',
    path: ['dateTo'],
  });

export type AppointmentListFilterInput = z.infer<typeof appointmentListFilterSchema>;

export function validateAppointmentFilterInput(
  input: Partial<AppointmentListFilterParams>,
): { success: true; data: AppointmentListFilterInput } | { success: false; errorMessage: string } {
  const parsed = appointmentListFilterSchema.safeParse(input);
  if (!parsed.success) {
    const firstIssue = parsed.error.issues[0];
    return {
      success: false,
      errorMessage: firstIssue?.message ?? 'Thông tin bộ lọc lịch hẹn không hợp lệ.',
    };
  }
  return {
    success: true,
    data: parsed.data,
  };
}
