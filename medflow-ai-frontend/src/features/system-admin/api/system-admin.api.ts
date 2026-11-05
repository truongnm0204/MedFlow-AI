import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  SYSTEM_ADMIN_DASHBOARD_MOCK,
  SYSTEM_ADMIN_HOSPITAL_OPTIONS,
} from '../data/system-admin-mock-data';
import type {
  GetSystemAdminDashboardParams,
  SystemAdminDashboardResponseDto,
} from '../types/system-admin.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function applyMockFilters(
  base: SystemAdminDashboardResponseDto,
  params?: GetSystemAdminDashboardParams,
): SystemAdminDashboardResponseDto {
  const limit = params?.recentAuditLimit ?? 10;
  const hospitalId = params?.hospitalId ?? null;

  const filteredLogs = base.recentAuditLogs
    .filter((log) => !hospitalId || log.hospitalId === hospitalId)
    .slice(0, limit);

  const hospitalOption = hospitalId
    ? SYSTEM_ADMIN_HOSPITAL_OPTIONS.find((h) => h.id === hospitalId)
    : null;

  return {
    ...base,
    adminProfile: {
      ...base.adminProfile,
      hospitalId,
      hospitalName: hospitalOption?.name ?? base.adminProfile.hospitalName,
    },
    recentAuditLogs: filteredLogs,
  };
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của System Admin (`GET /admin/system-dashboard`).
 * Hỗ trợ query params `recentAuditLimit` (1..50) và `hospitalId` (tùy chọn).
 * Khi không có token thật trong localStorage hoặc backend offline/404, fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (400/401/403/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchSystemAdminDashboard(
  params: GetSystemAdminDashboardParams = {},
): Promise<SystemAdminDashboardResponseDto> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const queryParams: Record<string, number> = {
        recentAuditLimit: params.recentAuditLimit ?? 10,
      };
      if (params.hospitalId) {
        queryParams.hospitalId = params.hospitalId;
      }

      const { data } = await apiClient.get<SystemAdminDashboardResponseDto>(
        '/admin/system-dashboard',
        { params: queryParams },
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return applyMockFilters(SYSTEM_ADMIN_DASHBOARD_MOCK, params);
}
