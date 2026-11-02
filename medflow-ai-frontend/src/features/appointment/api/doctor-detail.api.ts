import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { getDoctorDetailById } from '../data/doctor-detail-mock';
import type { DoctorDetailProfile } from '../types/doctor-detail.types';

/** Chỉ fallback về mock khi backend chưa có bản ghi demo mở rộng (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if (import.meta.env.MODE !== 'test') return true;
  return 'mock' in apiClient.get;
}

export async function fetchDoctorDetailApi(doctorId: string): Promise<DoctorDetailProfile> {
  const normalized = doctorId.trim();
  if (!normalized) {
    throw new Error('Mã bác sĩ không được để trống');
  }

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<DoctorDetailProfile>(
        `/doctors/${encodeURIComponent(normalized)}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const profile = getDoctorDetailById(normalized);
  if (!profile) {
    throw new Error('Không tìm thấy thông tin hồ sơ bác sĩ');
  }
  return profile;
}
