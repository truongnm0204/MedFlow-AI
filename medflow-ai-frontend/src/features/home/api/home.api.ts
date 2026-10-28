import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { filterMockQuickSlots, MOCK_PATIENT_HOME_SUMMARY } from '../data/patient-home-mock';
import type {
  PatientHomeSummary,
  QuickSlotItem,
  QuickSlotSearchRequest,
} from '../types/home.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Patient (`GET /patients/me/home`).
 * Khi backend M1-010 chưa triển khai (404/network), fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (403/422/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchPatientHomeSummary(): Promise<PatientHomeSummary> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<PatientHomeSummary>('/patients/me/home');
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return MOCK_PATIENT_HOME_SUMMARY;
}

/**
 * Tra cứu nhanh khung giờ khám khả dụng theo chuyên khoa, bác sĩ và ngày khám.
 */
export async function searchQuickSlots(req: QuickSlotSearchRequest): Promise<QuickSlotItem[]> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.post<QuickSlotItem[]>('/appointments/quick-slots', req);
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return filterMockQuickSlots(req);
}
