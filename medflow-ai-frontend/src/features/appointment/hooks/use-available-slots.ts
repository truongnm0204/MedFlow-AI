import { useQuery } from '@tanstack/react-query';

import { getHospitalTodayIso } from '@/shared/lib/hospital-date';

import {
  fetchAvailableAppointmentSlots,
  fetchServiceAvailableShifts,
  type FetchAvailableSlotsParams,
  type FetchServiceShiftsParams,
} from '../api/slot-booking.api';

export function useAvailableSlots(
  params: FetchAvailableSlotsParams,
  options?: { enabled?: boolean },
) {
  return useQuery({
    queryKey: [
      'appointment-available-slots',
      params.doctorId ?? 'default',
      params.serviceId ?? 'default',
      params.dateIso ?? getHospitalTodayIso(),
    ],
    queryFn: () => fetchAvailableAppointmentSlots(params),
    enabled: options?.enabled ?? true,
  });
}

export function useServiceAvailableShifts(
  params: FetchServiceShiftsParams,
  options?: { enabled?: boolean },
) {
  return useQuery({
    queryKey: [
      'appointment-service-shifts',
      params.serviceId ?? 'default',
      params.dateIso ?? getHospitalTodayIso(),
    ],
    queryFn: () => fetchServiceAvailableShifts(params),
    enabled: options?.enabled ?? true,
  });
}
