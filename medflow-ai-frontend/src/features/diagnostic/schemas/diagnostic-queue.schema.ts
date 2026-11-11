import { z } from 'zod';

import type {
  DiagnosticQueueCategoryFilter,
  DiagnosticQueuePresenceFilter,
  DiagnosticQueuePriorityFilter,
  DiagnosticQueueStatusFilter,
  GetDiagnosticQueueParams,
} from '../types/diagnostic-queue.types';

export const DIAGNOSTIC_QUEUE_CATEGORIES = ['ALL', 'LABORATORY', 'IMAGING', 'FUNCTIONAL'] as const;

export const DIAGNOSTIC_QUEUE_STATUS_FILTERS = [
  'ALL',
  'RESERVED',
  'WAITING',
  'READY',
  'CALLED',
  'IN_PROGRESS',
  'COMPLETED',
  'SKIPPED',
  'CANCELLED',
] as const;

export const DIAGNOSTIC_QUEUE_TARGET_STATUSES = [
  'WAITING',
  'READY',
  'CALLED',
  'IN_PROGRESS',
  'COMPLETED',
  'SKIPPED',
  'CANCELLED',
] as const;

export const DIAGNOSTIC_QUEUE_PRIORITIES = [
  'ALL',
  'STAT',
  'URGENT',
  'ELDERLY',
  'PREGNANT',
  'NORMAL',
] as const;

export const DIAGNOSTIC_QUEUE_PRESENCE_FILTERS = ['ALL', 'PRESENT', 'NOT_ARRIVED', 'LEFT'] as const;

export const DIAGNOSTIC_QUEUE_PRESENCE_STATUSES = ['PRESENT', 'NOT_ARRIVED', 'LEFT'] as const;

export const MISSED_TURN_REASON_PRESETS = [
  'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
  'Bệnh nhân chưa nhịn ăn đủ 8 giờ, tạm hoãn lấy mẫu xét nghiệm.',
  'Bệnh nhân đang thực hiện kỹ thuật ở phòng cận lâm sàng khác cùng đợt khám.',
  'Bệnh nhân xin ra ngoài tạm thời, chờ quay lại xác nhận.',
] as const;

export const CANCEL_QUEUE_REASON_PRESETS = [
  'Bác sĩ khám lâm sàng thay đổi phác đồ và hủy chỉ định cận lâm sàng.',
  'Bệnh nhân từ chối thực hiện kỹ thuật cận lâm sàng và đã ký xác nhận.',
  'Phiếu chỉ định cận lâm sàng bị trùng lặp trên cùng lượt khám.',
  'Thiết bị kỹ thuật đang bảo trì đột xuất, điều phối sang cơ sở khác.',
] as const;

const ISO_DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

function isSafeText(value: string): boolean {
  return !value.includes('<') && !value.includes('>');
}

export const diagnosticQueueFilterSchema = z.object({
  queueDate: z
    .string()
    .trim()
    .refine(
      (val) => !val || val.toUpperCase() === 'ALL' || ISO_DATE_REGEX.test(val),
      'Ngày hàng đợi cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).',
    ),
  servicePointId: z
    .string()
    .trim()
    .max(100, "Giá trị bộ lọc 'ServicePointId' không hợp lệ.")
    .refine(isSafeText, "Giá trị bộ lọc 'ServicePointId' không hợp lệ."),
  category: z.enum(DIAGNOSTIC_QUEUE_CATEGORIES, {
    message: 'Nhóm cận lâm sàng lọc không hợp lệ.',
  }),
  queueStatus: z.enum(DIAGNOSTIC_QUEUE_STATUS_FILTERS, {
    message: 'Trạng thái hàng đợi cận lâm sàng lọc không hợp lệ.',
  }),
  priority: z.enum(DIAGNOSTIC_QUEUE_PRIORITIES, {
    message: 'Mức độ ưu tiên lọc không hợp lệ.',
  }),
  presenceStatus: z.enum(DIAGNOSTIC_QUEUE_PRESENCE_FILTERS, {
    message: 'Trạng thái hiện diện lọc không hợp lệ.',
  }),
  activeOnly: z.boolean(),
  requiredForConclusion: z.boolean(),
  keyword: z
    .string()
    .trim()
    .max(100, 'Từ khóa tìm kiếm không được vượt quá 100 ký tự.')
    .refine(isSafeText, 'Từ khóa tìm kiếm chứa ký tự không hợp lệ.'),
  limit: z
    .number()
    .int()
    .min(1, 'Số lượng lượt xếp hàng cận lâm sàng hiển thị phải từ 1 đến 100.')
    .max(100, 'Số lượng lượt xếp hàng cận lâm sàng hiển thị phải từ 1 đến 100.'),
});

export type DiagnosticQueueFilterFormValues = z.infer<typeof diagnosticQueueFilterSchema>;

export const DEFAULT_DIAGNOSTIC_QUEUE_FILTERS: DiagnosticQueueFilterFormValues = {
  queueDate: '',
  servicePointId: 'ALL',
  category: 'ALL',
  queueStatus: 'ALL',
  priority: 'ALL',
  presenceStatus: 'ALL',
  activeOnly: false,
  requiredForConclusion: false,
  keyword: '',
  limit: 100,
};

export function toDiagnosticQueueQueryParams(
  filters: DiagnosticQueueFilterFormValues,
): GetDiagnosticQueueParams {
  const params: GetDiagnosticQueueParams = {
    limit: filters.limit,
  };

  if (filters.queueDate.trim() && filters.queueDate.trim().toUpperCase() !== 'ALL') {
    params.queueDate = filters.queueDate.trim();
  }
  if (filters.servicePointId.trim() && filters.servicePointId.trim().toUpperCase() !== 'ALL') {
    params.servicePointId = filters.servicePointId.trim();
  }
  if (filters.category !== 'ALL') {
    params.category = filters.category as DiagnosticQueueCategoryFilter;
  }
  if (filters.queueStatus !== 'ALL') {
    params.queueStatus = filters.queueStatus as DiagnosticQueueStatusFilter;
  }
  if (filters.priority !== 'ALL') {
    params.priority = filters.priority as DiagnosticQueuePriorityFilter;
  }
  if (filters.presenceStatus !== 'ALL') {
    params.presenceStatus = filters.presenceStatus as DiagnosticQueuePresenceFilter;
  }
  if (filters.activeOnly) {
    params.activeOnly = true;
  }
  if (filters.requiredForConclusion) {
    params.requiredForConclusion = true;
  }
  if (filters.keyword.trim()) {
    params.keyword = filters.keyword.trim();
  }

  return params;
}

export const callNextDiagnosticPatientSchema = z.object({
  servicePointId: z
    .string()
    .trim()
    .min(1, 'Mã điểm phục vụ cận lâm sàng (ServicePointId) không hợp lệ.')
    .max(64, 'Mã điểm phục vụ cận lâm sàng (ServicePointId) không hợp lệ.')
    .refine(isSafeText, 'Mã điểm phục vụ cận lâm sàng (ServicePointId) không hợp lệ.'),
  category: z.enum(DIAGNOSTIC_QUEUE_CATEGORIES).nullable().optional(),
  notes: z
    .string()
    .trim()
    .max(500, 'Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).')
    .refine(isSafeText, 'Ghi chú điều phối hàng đợi chứa ký tự không hợp lệ.')
    .nullable()
    .optional(),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const missedTurnDiagnosticQueueSchema = z.object({
  reason: z
    .string()
    .trim()
    .min(5, 'Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.')
    .max(500, 'Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.')
    .refine(isSafeText, 'Lý do đánh dấu qua lượt chứa ký tự không hợp lệ.'),
  presenceStatus: z
    .enum(DIAGNOSTIC_QUEUE_PRESENCE_STATUSES, {
      message: 'Trạng thái hiện diện không hợp lệ.',
    })
    .nullable()
    .optional(),
  notes: z
    .string()
    .trim()
    .max(500, 'Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).')
    .refine(isSafeText, 'Ghi chú điều phối hàng đợi chứa ký tự không hợp lệ.')
    .nullable()
    .optional(),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const restoreTurnDiagnosticQueueSchema = z.object({
  reason: z
    .string()
    .trim()
    .max(500, 'Lý do khôi phục lượt không được vượt quá 500 ký tự.')
    .refine(isSafeText, 'Lý do khôi phục lượt chứa ký tự không hợp lệ.')
    .nullable()
    .optional(),
  presenceStatus: z
    .enum(DIAGNOSTIC_QUEUE_PRESENCE_STATUSES, {
      message: 'Trạng thái hiện diện không hợp lệ.',
    })
    .nullable()
    .optional(),
  notes: z
    .string()
    .trim()
    .max(500, 'Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).')
    .refine(isSafeText, 'Ghi chú điều phối hàng đợi chứa ký tự không hợp lệ.')
    .nullable()
    .optional(),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const cancelDiagnosticQueueEntrySchema = z.object({
  reason: z
    .string()
    .trim()
    .min(5, 'Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.')
    .max(500, 'Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.')
    .refine(isSafeText, 'Lý do hủy lượt xếp hàng chứa ký tự không hợp lệ.'),
  notes: z
    .string()
    .trim()
    .max(500, 'Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).')
    .refine(isSafeText, 'Ghi chú điều phối hàng đợi chứa ký tự không hợp lệ.')
    .nullable()
    .optional(),
  cancelLinkedOrder: z.boolean().default(false),
  expectedStateVersion: z
    .number()
    .int()
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
});

export const updateDiagnosticQueueStatusSchema = z
  .object({
    targetStatus: z.enum(DIAGNOSTIC_QUEUE_TARGET_STATUSES, {
      message: 'Trạng thái hàng đợi cận lâm sàng đích không hợp lệ.',
    }),
    presenceStatus: z
      .enum(DIAGNOSTIC_QUEUE_PRESENCE_STATUSES, {
        message: 'Trạng thái hiện diện không hợp lệ.',
      })
      .nullable()
      .optional(),
    servicePointId: z.number().int().min(1).nullable().optional(),
    roomId: z.number().int().min(1).nullable().optional(),
    reason: z
      .string()
      .trim()
      .max(500, 'Lý do cập nhật trạng thái không được vượt quá 500 ký tự.')
      .refine(isSafeText, 'Lý do cập nhật trạng thái chứa ký tự không hợp lệ.')
      .nullable()
      .optional(),
    notes: z
      .string()
      .trim()
      .max(500, 'Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).')
      .refine(isSafeText, 'Ghi chú điều phối hàng đợi chứa ký tự không hợp lệ.')
      .nullable()
      .optional(),
    cancelLinkedOrder: z.boolean().nullable().optional(),
    expectedStateVersion: z
      .number()
      .int()
      .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    const trimmedReason = data.reason?.trim() ?? '';
    if (data.targetStatus === 'CANCELLED') {
      if (trimmedReason.length < 5 || trimmedReason.length > 500) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['reason'],
          message: 'Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.',
        });
      }
    } else if (
      data.targetStatus === 'SKIPPED' &&
      trimmedReason.length > 0 &&
      trimmedReason.length < 5
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['reason'],
        message: 'Lý do đánh dấu qua lượt phải từ 5 đến 500 ký tự.',
      });
    }
  });
