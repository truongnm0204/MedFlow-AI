import { apiClient } from '@/shared/lib/axios';

import type {
  AvailableSlotsPayload,
  ServiceAvailableShiftsPayload,
} from '../types/slot-booking.types';

export interface FetchAvailableSlotsParams {
  doctorId?: string | null;
  serviceId?: string | null;
  dateIso?: string;
}

export interface FetchServiceShiftsParams {
  serviceId?: string | null;
  dateIso?: string;
}

export async function fetchAvailableAppointmentSlots(
  params: FetchAvailableSlotsParams,
): Promise<AvailableSlotsPayload> {
  const { data } = await apiClient.get<AvailableSlotsPayload>('/appointments/available-slots', {
    params,
  });
  return data;
}

export async function fetchServiceAvailableShifts(
  params: FetchServiceShiftsParams,
): Promise<ServiceAvailableShiftsPayload> {
  const { data } = await apiClient.get<ServiceAvailableShiftsPayload>(
    '/appointments/service-shifts',
    { params },
  );
  return data;
}
