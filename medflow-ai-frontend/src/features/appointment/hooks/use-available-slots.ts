import { useQuery } from '@tanstack/react-query';

import {
  fetchAvailableAppointmentSlots,
  fetchServiceAvailableShifts,
  type FetchAvailableSlotsParams,
  type FetchServiceShiftsParams,
} from '../api/slot-booking.api';

export function useAvailableSlots(params: FetchAvailableSlotsParams) {
  return useQuery({
    queryKey: [
      'appointment-available-slots',
      params.doctorId ?? 'default',
      params.serviceId ?? 'default',
      params.dateIso ?? '2026-10-13',
    ],
    queryFn: () => fetchAvailableAppointmentSlots(params),
  });
}

export function useServiceAvailableShifts(params: FetchServiceShiftsParams) {
  return useQuery({
    queryKey: [
      'appointment-service-shifts',
      params.serviceId ?? 'default',
      params.dateIso ?? '2026-10-13',
    ],
    queryFn: () => fetchServiceAvailableShifts(params),
  });
}
