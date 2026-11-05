import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { PHARMACY_DASHBOARD_MOCK } from '../data/pharmacy-mock-data';
import type {
  PharmacyDashboardQueryParams,
  PharmacyDashboardSummary,
} from '../types/pharmacy.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Dược sĩ (`GET /pharmacy/dashboard/summary`).
 * Khi backend M1-018 chưa triển khai (404/network), fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (401/403/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchPharmacyDashboardSummary(
  params?: PharmacyDashboardQueryParams,
): Promise<PharmacyDashboardSummary> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<PharmacyDashboardSummary>(
        '/pharmacy/dashboard/summary',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return PHARMACY_DASHBOARD_MOCK;
}
