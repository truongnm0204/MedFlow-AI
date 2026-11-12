import { z } from 'zod';

import type { DoctorDiagnosticTrackingQueryParams } from '../types/doctor-diagnostic-tracking.types';

function isValidIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const [yearStr, monthStr, dayStr] = value.split('-');
  const year = Number(yearStr);
  const month = Number(monthStr);
  const day = Number(dayStr);
  const parsed = new Date(Date.UTC(year, month - 1, day));
  return (
    parsed.getUTCFullYear() === year &&
    parsed.getUTCMonth() === month - 1 &&
    parsed.getUTCDate() === day
  );
}

const KEYWORD_ERROR_MESSAGE =
  'Từ khóa tìm kiếm không hợp lệ (tối đa 100 ký tự, không chứa ký tự < >).';

export const doctorDiagnosticTrackingFilterSchema = z.object({
  date: z
    .string()
    .trim()
    .refine(
      (value) => value === '' || isValidIsoDate(value),
      'Ngày theo dõi kết quả cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).',
    ),
  status: z.enum(['ALL', 'COMPLETED', 'IN_PROGRESS', 'PENDING_SIGN']),
  keyword: z
    .string()
    .trim()
    .max(100, KEYWORD_ERROR_MESSAGE)
    .refine((value) => !/[<>]/.test(value), KEYWORD_ERROR_MESSAGE),
  page: z.number().int().min(1, 'Số trang phải lớn hơn hoặc bằng 1.'),
});

export type DoctorDiagnosticTrackingFilterValues = z.infer<
  typeof doctorDiagnosticTrackingFilterSchema
>;

export const DEFAULT_DOCTOR_DIAGNOSTIC_TRACKING_FILTERS: DoctorDiagnosticTrackingFilterValues = {
  date: '',
  status: 'ALL',
  keyword: '',
  page: 1,
};

export function toDoctorDiagnosticTrackingQueryParams(
  values: DoctorDiagnosticTrackingFilterValues,
): DoctorDiagnosticTrackingQueryParams {
  const params: DoctorDiagnosticTrackingQueryParams = {
    page: values.page,
    pageSize: 20,
  };

  const trimmedDate = values.date.trim();
  if (trimmedDate !== '') {
    params.date = trimmedDate;
  }

  if (values.status !== 'ALL') {
    params.status = values.status;
  }

  const trimmedKeyword = values.keyword.trim();
  if (trimmedKeyword !== '') {
    params.keyword = trimmedKeyword;
  }

  return params;
}
