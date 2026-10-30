import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  cancelAppointmentApi,
  fetchAppointmentDetailByIdApi,
  rescheduleAppointmentApi,
  updateAppointmentReasonApi,
} from '../api/appointment-detail.api';

export function useAppointmentDetailQuery(appointmentId: string) {
  return useQuery({
    queryKey: ['appointments', 'detail', appointmentId],
    queryFn: () => fetchAppointmentDetailByIdApi(appointmentId),
    enabled: Boolean(appointmentId.trim()),
    retry: false,
  });
}

export function useRescheduleAppointmentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: rescheduleAppointmentApi,
    onSuccess: (updated) => {
      queryClient.setQueryData(['appointments', 'detail', updated.id], updated);
      queryClient.setQueryData(['appointments', 'detail', updated.appointmentCode], updated);
      queryClient.invalidateQueries({ queryKey: ['appointments'] });
    },
  });
}

export function useCancelAppointmentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: cancelAppointmentApi,
    onSuccess: (updated) => {
      queryClient.setQueryData(['appointments', 'detail', updated.id], updated);
      queryClient.setQueryData(['appointments', 'detail', updated.appointmentCode], updated);
      queryClient.invalidateQueries({ queryKey: ['appointments'] });
    },
  });
}

export function useUpdateAppointmentReasonMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: updateAppointmentReasonApi,
    onSuccess: (updated) => {
      queryClient.setQueryData(['appointments', 'detail', updated.id], updated);
      queryClient.setQueryData(['appointments', 'detail', updated.appointmentCode], updated);
      queryClient.invalidateQueries({ queryKey: ['appointments'] });
    },
  });
}
