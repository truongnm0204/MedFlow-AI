import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DOCTOR_ASSIGNED_QUEUE_MOCK } from '../data/doctor-assigned-queue-mock';
import type {
  DoctorAssignedQueueQueryParams,
  DoctorAssignedQueueResponseDto,
} from '../types/doctor-assigned-queue.types';

/** Chỉ fallback về mock khi offline/network error (endpoint thực tế đã có và trả 404 cho lỗi nghiệp vụ). */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response;
}

/**
 * Lấy danh sách hàng đợi được phân công của Bác sĩ (`GET /doctor/assigned-queue`).
 * Chỉ fallback về mock khi chưa có access token hoặc lỗi mạng không có HTTP response;
 * rethrow mọi HTTP error (kể cả 404 domain error) để UI hiển thị cảnh báo.
 */
export async function fetchDoctorAssignedQueue(
  params?: DoctorAssignedQueueQueryParams,
): Promise<DoctorAssignedQueueResponseDto> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<DoctorAssignedQueueResponseDto>(
        '/doctor/assigned-queue',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return DOCTOR_ASSIGNED_QUEUE_MOCK;
}
