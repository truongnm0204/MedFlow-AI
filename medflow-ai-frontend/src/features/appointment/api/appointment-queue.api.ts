import { apiClient } from '@/shared/lib/axios';

import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

export async function fetchAppointmentQueueStatusApi(
  appointmentId: string,
): Promise<AppointmentQueueStatus> {
  const cleanId = appointmentId.trim();
  if (!cleanId) {
    throw new Error('Mã lịch hẹn không hợp lệ.');
  }

  const { data } = await apiClient.get<AppointmentQueueStatus>(
    `/appointments/${encodeURIComponent(cleanId)}/queue-status`,
  );
  return data;
}
