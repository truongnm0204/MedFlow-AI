import { apiClient } from '@/shared/lib/axios';

import {
  getMockAvailableSlotsPayload,
  getMockServiceAvailableShiftsPayload,
} from '../data/slot-booking-mock';
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
  try {
    const { data } = await apiClient.get<AvailableSlotsPayload>('/appointments/available-slots', {
      params,
    });
    if (data && Array.isArray(data.morningSlots)) {
      return data;
    }
    return getMockAvailableSlotsPayload(params.doctorId, params.serviceId, params.dateIso);
  } catch {
    return getMockAvailableSlotsPayload(params.doctorId, params.serviceId, params.dateIso);
  }
}

export async function fetchServiceAvailableShifts(
  params: FetchServiceShiftsParams,
): Promise<ServiceAvailableShiftsPayload> {
  try {
    const { data } = await apiClient.get<ServiceAvailableShiftsPayload>(
      '/appointments/service-shifts',
      { params },
    );
    if (data && Array.isArray(data.shifts)) {
      return data;
    }
    return getMockServiceAvailableShiftsPayload(params.serviceId, params.dateIso);
  } catch {
    return getMockServiceAvailableShiftsPayload(params.serviceId, params.dateIso);
  }
}
