import { apiClient } from '@/shared/lib/axios';

import type {
  AppointmentPolicyDto,
  UpdateAppointmentPolicyRequest,
} from '../types/appointment-policy.types';

export async function fetchAppointmentPolicyApi(): Promise<AppointmentPolicyDto> {
  const { data } = await apiClient.get<AppointmentPolicyDto>('/admin/appointment-policy');
  return data;
}

export async function updateAppointmentPolicyApi(
  payload: UpdateAppointmentPolicyRequest,
): Promise<AppointmentPolicyDto> {
  const { data } = await apiClient.put<AppointmentPolicyDto>('/admin/appointment-policy', payload);
  return data;
}
