import { apiClient } from '@/shared/lib/axios';

import type { DoctorDashboardQueryParams, DoctorDashboardSummary } from '../types/doctor.types';

/**
 * Lấy dữ liệu tổng quan bàn khám Bác sĩ (`GET /doctor/dashboard/summary`).
 */
export async function fetchDoctorDashboardSummary(
  params?: DoctorDashboardQueryParams,
): Promise<DoctorDashboardSummary> {
  const { data } = await apiClient.get<DoctorDashboardSummary>(
    '/doctor/dashboard/summary',
    params ? { params } : undefined,
  );
  return data;
}
