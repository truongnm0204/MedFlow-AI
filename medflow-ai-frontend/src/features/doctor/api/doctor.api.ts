import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DOCTOR_DASHBOARD_MOCK } from '../data/doctor-mock-data';
import type { DoctorDashboardQueryParams, DoctorDashboardSummary } from '../types/doctor.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/**
 * Lấy dữ liệu tổng quan bàn khám Bác sĩ (`GET /doctor/dashboard/summary`).
 * Khi môi trường test không bật backend (404/network), fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (400/401/403/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchDoctorDashboardSummary(
  params?: DoctorDashboardQueryParams,
): Promise<DoctorDashboardSummary> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<DoctorDashboardSummary>(
        '/doctor/dashboard/summary',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return DOCTOR_DASHBOARD_MOCK;
}
