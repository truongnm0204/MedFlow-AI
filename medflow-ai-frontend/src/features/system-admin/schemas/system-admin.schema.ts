import { z } from 'zod';

import type {
  AuditActionCategory,
  GetSystemAdminDashboardParams,
} from '../types/system-admin.types';

export const systemAdminDashboardFilterSchema = z.object({
  recentAuditLimit: z
    .number({ error: 'Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.' })
    .int('Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.')
    .min(1, 'Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.')
    .max(50, 'Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.'),
  hospitalId: z
    .number({ error: 'Mã bệnh viện lọc không hợp lệ.' })
    .int('Mã bệnh viện lọc không hợp lệ.')
    .positive('Mã bệnh viện lọc không hợp lệ.')
    .nullable()
    .optional(),
});

export interface SystemAdminFilterFormValidationResult {
  isValid: boolean;
  params?: GetSystemAdminDashboardParams;
  errorMessage?: string;
}

export function validateSystemAdminFilterInputs(
  recentAuditLimitInput: string,
  hospitalIdInput: string,
): SystemAdminFilterFormValidationResult {
  const trimmedLimit = recentAuditLimitInput.trim();
  const parsedLimit = trimmedLimit === '' ? Number.NaN : Number(trimmedLimit);

  const trimmedHospital = hospitalIdInput.trim();
  const parsedHospital = trimmedHospital === '' ? null : Number(trimmedHospital);

  const result = systemAdminDashboardFilterSchema.safeParse({
    recentAuditLimit: parsedLimit,
    hospitalId: parsedHospital,
  });

  if (!result.success) {
    return {
      isValid: false,
      errorMessage: result.error.issues[0]?.message ?? 'Tham số lọc bảng điều khiển không hợp lệ.',
    };
  }

  return {
    isValid: true,
    params: {
      recentAuditLimit: result.data.recentAuditLimit,
      hospitalId: result.data.hospitalId ?? null,
    },
  };
}

export function classifyAuditAction(action: string): AuditActionCategory {
  const upper = action.toUpperCase();
  if (
    upper.includes('AUTH') ||
    upper.includes('LOGIN') ||
    upper.includes('LOGOUT') ||
    upper.includes('OTP') ||
    upper.includes('REFRESH')
  ) {
    return 'AUTH';
  }
  if (
    upper.includes('ROLE') ||
    upper.includes('PERMISSION') ||
    upper.includes('STAFF') ||
    upper.includes('USER')
  ) {
    return 'RBAC';
  }
  return 'CONFIG';
}
