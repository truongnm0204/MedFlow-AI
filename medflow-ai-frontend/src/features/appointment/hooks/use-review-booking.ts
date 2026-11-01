import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  confirmAppointmentBooking,
  fetchAppointmentReviewPreview,
} from '../api/review-booking.api';
import type {
  ConfirmBookingRequest,
  ReviewBookingQueryParams,
} from '../types/review-booking.types';

export function useAppointmentReviewPreview(params: ReviewBookingQueryParams) {
  return useQuery({
    queryKey: [
      'appointment-review-preview',
      params.mode ?? 'doctor',
      params.doctorId ?? 'default',
      params.serviceId ?? 'default',
      params.serviceCode ?? 'default',
      params.patientId ?? 'pat-self',
      params.dateIso ?? '2026-10-13',
      params.slotId ?? 'slot-0900',
      params.shiftId ?? 'shift-morning',
    ],
    queryFn: () => fetchAppointmentReviewPreview(params),
  });
}

export function useConfirmAppointmentBooking() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: ConfirmBookingRequest) => confirmAppointmentBooking(payload),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: ['appointments'] });
      queryClient.invalidateQueries({ queryKey: ['appointment-detail', response.appointmentId] });
    },
  });
}
