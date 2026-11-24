import { apiClient } from '@/shared/lib/axios';

import type {
  PatientHomeSummary,
  QuickSlotItem,
  QuickSlotSearchRequest,
} from '../types/home.types';

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Patient (`GET /patients/me/home`).
 */
export async function fetchPatientHomeSummary(): Promise<PatientHomeSummary> {
  const { data } = await apiClient.get<PatientHomeSummary>('/patients/me/home');
  return data;
}

/**
 * Tra cứu nhanh khung giờ khám khả dụng theo chuyên khoa, bác sĩ và ngày khám.
 */
export async function searchQuickSlots(req: QuickSlotSearchRequest): Promise<QuickSlotItem[]> {
  const { data } = await apiClient.post<QuickSlotItem[]>('/appointments/quick-slots', req);
  return data;
}
