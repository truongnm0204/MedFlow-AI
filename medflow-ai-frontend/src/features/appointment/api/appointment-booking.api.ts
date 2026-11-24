import { apiClient } from '@/shared/lib/axios';

import type { AppointmentListItem } from '../types/appointment-list.types';
import type { CreateAppointmentRequest } from '../types/booking-review.types';

export async function createAppointmentApi(
  request: CreateAppointmentRequest,
): Promise<AppointmentListItem> {
  const { data } = await apiClient.post<AppointmentListItem>('/appointments', request);
  return data;
}
