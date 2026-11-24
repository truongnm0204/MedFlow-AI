import { apiClient } from '@/shared/lib/axios';

import type {
  DoctorAssignedQueueQueryParams,
  DoctorAssignedQueueResponseDto,
  DoctorCallNextRequestPayload,
  DoctorCallNextResponseDto,
} from '../types/doctor-assigned-queue.types';

/**
 * Lấy danh sách hàng đợi được phân công của Bác sĩ (`GET /doctor/assigned-queue`).
 */
export async function fetchDoctorAssignedQueue(
  params?: DoctorAssignedQueueQueryParams,
): Promise<DoctorAssignedQueueResponseDto> {
  const { data } = await apiClient.get<DoctorAssignedQueueResponseDto>(
    '/doctor/assigned-queue',
    params ? { params } : undefined,
  );
  return data;
}

export async function callNextDoctorPatient(
  servicePointId: string,
  payload?: DoctorCallNextRequestPayload,
): Promise<DoctorCallNextResponseDto> {
  const encoded = encodeURIComponent(servicePointId.trim());
  const body = payload?.notes !== undefined ? { notes: payload.notes } : {};
  const { data } = await apiClient.post<DoctorCallNextResponseDto>(
    `/queue/service-points/${encoded}/call-next`,
    body,
  );
  return data;
}
