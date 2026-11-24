import { apiClient } from '@/shared/lib/axios';

import type {
  HospitalAdminDashboardQueryParams,
  HospitalAdminDashboardSummary,
} from '../types/hospital-admin.types';

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Hospital Admin (`GET /admin/hospital-dashboard/summary`).
 */
export async function fetchHospitalAdminDashboardSummary(
  params?: HospitalAdminDashboardQueryParams,
): Promise<HospitalAdminDashboardSummary> {
  const { data } = await apiClient.get<HospitalAdminDashboardSummary>(
    '/admin/hospital-dashboard/summary',
    params ? { params } : undefined,
  );
  return data;
}
