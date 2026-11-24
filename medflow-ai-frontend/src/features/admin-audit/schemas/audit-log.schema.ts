import { z } from 'zod';

import type { AuditLogFilterParams } from '../types/audit-log.types';

const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/;

export const auditLogFilterFormSchema = z
  .object({
    keyword: z.string().max(200, 'Từ khóa tìm kiếm không được vượt quá 200 ký tự.'),
    action: z.string().max(100, 'Mã hành động không được vượt quá 100 ký tự.'),
    entityType: z.string().max(100, 'Loại đối tượng không được vượt quá 100 ký tự.'),
    actorUserIdInput: z.string().trim(),
    hospitalIdInput: z.string().trim(),
    fromDate: z.string().trim(),
    toDate: z.string().trim(),
  })
  .superRefine((values, ctx) => {
    if (values.actorUserIdInput !== '') {
      const parsedActor = Number(values.actorUserIdInput);
      if (!Number.isInteger(parsedActor) || parsedActor <= 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['actorUserIdInput'],
          message: 'Mã người thực hiện phải là số nguyên dương.',
        });
      }
    }

    if (values.hospitalIdInput !== '') {
      const parsedHospital = Number(values.hospitalIdInput);
      if (!Number.isInteger(parsedHospital) || parsedHospital <= 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['hospitalIdInput'],
          message: 'Mã cơ sở y tế lọc không hợp lệ.',
        });
      }
    }

    if (values.fromDate !== '' && !isoDatePattern.test(values.fromDate)) {
      ctx.addIssue({
        code: 'custom',
        path: ['fromDate'],
        message: 'Ngày bắt đầu phải đúng định dạng YYYY-MM-DD.',
      });
    }

    if (values.toDate !== '' && !isoDatePattern.test(values.toDate)) {
      ctx.addIssue({
        code: 'custom',
        path: ['toDate'],
        message: 'Ngày kết thúc phải đúng định dạng YYYY-MM-DD.',
      });
    }

    if (
      values.fromDate !== '' &&
      values.toDate !== '' &&
      isoDatePattern.test(values.fromDate) &&
      isoDatePattern.test(values.toDate)
    ) {
      if (values.fromDate > values.toDate) {
        ctx.addIssue({
          code: 'custom',
          path: ['fromDate'],
          message: 'Ngày bắt đầu tra cứu không được lớn hơn ngày kết thúc.',
        });
      } else {
        const startMs = Date.parse(`${values.fromDate}T00:00:00Z`);
        const endMs = Date.parse(`${values.toDate}T00:00:00Z`);
        const diffDays = Math.round((endMs - startMs) / (1000 * 60 * 60 * 24));
        if (diffDays > 366) {
          ctx.addIssue({
            code: 'custom',
            path: ['toDate'],
            message: 'Khoảng thời gian tra cứu nhật ký kiểm toán tối đa 366 ngày.',
          });
        }
      }
    }
  });

export type AuditLogFilterFormInputs = z.infer<typeof auditLogFilterFormSchema>;

export interface AuditLogFilterValidationResult {
  isValid: boolean;
  params?: Omit<AuditLogFilterParams, 'page' | 'pageSize'>;
  errorMessage?: string;
}

export function validateAuditLogFilterInputs(
  inputs: AuditLogFilterFormInputs,
): AuditLogFilterValidationResult {
  const parsed = auditLogFilterFormSchema.safeParse(inputs);
  if (!parsed.success) {
    return {
      isValid: false,
      errorMessage: parsed.error.issues[0]?.message ?? 'Bộ lọc nhật ký kiểm toán không hợp lệ.',
    };
  }

  const data = parsed.data;
  return {
    isValid: true,
    params: {
      keyword: data.keyword.trim() || undefined,
      action: data.action.trim() || undefined,
      entityType: data.entityType.trim() || undefined,
      actorUserId: data.actorUserIdInput !== '' ? Number(data.actorUserIdInput) : null,
      hospitalId: data.hospitalIdInput !== '' ? Number(data.hospitalIdInput) : null,
      fromDate: data.fromDate || undefined,
      toDate: data.toDate || undefined,
    },
  };
}

export function formatPrettyMetadataJson(rawJson: string | null | undefined): string {
  if (!rawJson || rawJson.trim() === '') {
    return '';
  }

  try {
    const parsed: unknown = JSON.parse(rawJson);
    return JSON.stringify(parsed, null, 2);
  } catch {
    return rawJson;
  }
}

export function formatHospitalDateTimeDisplay(localIso: string, utcIso?: string): string {
  const normalized = localIso?.trim();
  if (normalized) {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(normalized);
    if (match) {
      const [, y, m, d, hh, mm, ss] = match;
      return `${d}/${m}/${y} ${hh}:${mm}${ss ? `:${ss}` : ''} (UTC+7)`;
    }
  }

  if (utcIso) {
    const date = new Date(utcIso);
    if (!Number.isNaN(date.getTime())) {
      return new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false,
      }).format(date);
    }
  }

  return localIso || utcIso || 'Chưa xác định';
}
