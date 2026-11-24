import { apiClient } from '@/shared/lib/axios';

import type {
  GetSystemAdminDashboardParams,
  SystemAdminDashboardResponseDto,
} from '../types/system-admin.types';

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của System Admin (`GET /admin/system-dashboard`).
 * Hỗ trợ query params `recentAuditLimit` (1..50) và `hospitalId` (tùy chọn).
 */
export async function fetchSystemAdminDashboard(
  params: GetSystemAdminDashboardParams = {},
): Promise<SystemAdminDashboardResponseDto> {
  const queryParams: Record<string, number> = {
    recentAuditLimit: params.recentAuditLimit ?? 10,
  };
  if (params.hospitalId) {
    queryParams.hospitalId = params.hospitalId;
  }

  const { data } = await apiClient.get<SystemAdminDashboardResponseDto>('/admin/system-dashboard', {
    params: queryParams,
  });
  return data;
}
