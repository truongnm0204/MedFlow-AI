import { z } from 'zod';

import type { DiagnosticWorklistQueryParams } from '../types/diagnostic-worklist.types';

export const DIAGNOSTIC_WORKLIST_CATEGORIES = [
  'ALL',
  'LABORATORY',
  'IMAGING',
  'FUNCTIONAL',
] as const;

export const DIAGNOSTIC_WORKLIST_STATUSES = [
  'ALL',
  'ORDERED',
  'CONFIRMED',
  'IN_PROGRESS',
  'COMPLETED',
  'CANCELLED',
] as const;

export const DIAGNOSTIC_WORKLIST_QUEUE_STATUSES = [
  'ALL',
  'WAITING',
  'READY',
  'CALLED',
  'IN_PROGRESS',
  'COMPLETED',
  'SKIPPED',
  'CANCELLED',
] as const;

export const DIAGNOSTIC_WORKLIST_RESULT_STATUSES = [
  'ALL',
  'PENDING',
  'DRAFT',
  'PRELIMINARY',
  'PENDING_VERIFY',
  'FINALIZED',
  'VERIFIED',
  'CANCELLED',
] as const;

export const DIAGNOSTIC_WORKLIST_PRIORITIES = ['ALL', 'NORMAL', 'URGENT', 'STAT'] as const;

export const DIAGNOSTIC_WORKLIST_MANDATORY_OPTIONS = ['ALL', 'REQUIRED', 'OPTIONAL'] as const;

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

export const diagnosticWorklistFilterSchema = z.object({
  queueDate: z
    .string()
    .trim()
    .refine(
      (value) => value === '' || isValidIsoDate(value),
      'Ngày lọc danh sách chỉ định cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).',
    ),
  category: z.enum(DIAGNOSTIC_WORKLIST_CATEGORIES),
  status: z.enum(DIAGNOSTIC_WORKLIST_STATUSES),
  queueStatus: z.enum(DIAGNOSTIC_WORKLIST_QUEUE_STATUSES),
  resultStatus: z.enum(DIAGNOSTIC_WORKLIST_RESULT_STATUSES),
  priority: z.enum(DIAGNOSTIC_WORKLIST_PRIORITIES),
  servicePointId: z
    .string()
    .trim()
    .min(1, 'Điểm phục vụ cận lâm sàng lọc không hợp lệ.')
    .max(60, 'Điểm phục vụ cận lâm sàng lọc không hợp lệ.')
    .refine((value) => !/[<>]/.test(value), 'Điểm phục vụ cận lâm sàng lọc không hợp lệ.'),
  requiredForConclusion: z.enum(DIAGNOSTIC_WORKLIST_MANDATORY_OPTIONS),
  keyword: z
    .string()
    .trim()
    .max(100, 'Từ khóa tìm kiếm không được vượt quá 100 ký tự.')
    .refine((value) => !/[<>]/.test(value), 'Từ khóa tìm kiếm chứa ký tự không hợp lệ.'),
  page: z.number().int().min(1, 'Số trang hiển thị phải lớn hơn hoặc bằng 1.'),
  pageSize: z
    .number()
    .int()
    .min(1, 'Số lượng chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.')
    .max(100, 'Số lượng chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.'),
});

export type DiagnosticWorklistFilterFormValues = z.infer<typeof diagnosticWorklistFilterSchema>;

export const DEFAULT_DIAGNOSTIC_WORKLIST_FILTERS: DiagnosticWorklistFilterFormValues = {
  queueDate: '',
  category: 'ALL',
  status: 'ALL',
  queueStatus: 'ALL',
  resultStatus: 'ALL',
  priority: 'ALL',
  servicePointId: 'ALL',
  requiredForConclusion: 'ALL',
  keyword: '',
  page: 1,
  pageSize: 50,
};

function isValidDiagnosticOrderIdentifier(rawValue: string): boolean {
  const trimmed = rawValue.trim();
  if (!trimmed || trimmed.length > 60) {
    return false;
  }
  const normalized = trimmed.replace(/^#+/, '').trim();
  if (!normalized || normalized.startsWith('-')) {
    return false;
  }
  if (!/^[A-Za-z0-9._#-]+$/.test(normalized)) {
    return false;
  }
  if (/^\d+$/.test(normalized)) {
    return Number(normalized) > 0;
  }
  const prefixedDoMatch = /^do-(\d+)$/i.exec(normalized);
  if (prefixedDoMatch) {
    return Number(prefixedDoMatch[1]) > 0;
  }
  const prefixedQueueMatch = /^q-(\d+)$/i.exec(normalized);
  if (prefixedQueueMatch) {
    return Number(prefixedQueueMatch[1]) > 0;
  }
  return true;
}

export const diagnosticOrderIdentifierSchema = z
  .string()
  .trim()
  .min(1, 'Vui lòng cung cấp mã hoặc định danh phiếu chỉ định cận lâm sàng.')
  .max(60, 'Mã hoặc định danh phiếu chỉ định cận lâm sàng không hợp lệ.')
  .refine(
    isValidDiagnosticOrderIdentifier,
    'Mã hoặc định danh phiếu chỉ định cận lâm sàng không hợp lệ.',
  )
  .transform((value) => value.trim().replace(/^#+/, '').trim());

export function toDiagnosticWorklistQueryParams(
  filters: DiagnosticWorklistFilterFormValues,
): DiagnosticWorklistQueryParams {
  const params: DiagnosticWorklistQueryParams = {
    page: filters.page,
    pageSize: filters.pageSize,
  };

  if (filters.queueDate.trim()) {
    params.queueDate = filters.queueDate.trim();
  }
  if (filters.category !== 'ALL') {
    params.category = filters.category;
  }
  if (filters.status !== 'ALL') {
    params.status = filters.status;
  }
  if (filters.queueStatus !== 'ALL') {
    params.queueStatus = filters.queueStatus;
  }
  if (filters.resultStatus !== 'ALL') {
    params.resultStatus = filters.resultStatus;
  }
  if (filters.priority !== 'ALL') {
    params.priority = filters.priority;
  }
  if (filters.servicePointId && filters.servicePointId !== 'ALL') {
    params.servicePointId = filters.servicePointId.trim();
  }
  if (filters.requiredForConclusion === 'REQUIRED') {
    params.requiredForConclusion = true;
  } else if (filters.requiredForConclusion === 'OPTIONAL') {
    params.requiredForConclusion = false;
  }
  if (filters.keyword.trim()) {
    params.keyword = filters.keyword.trim();
  }

  return params;
}
