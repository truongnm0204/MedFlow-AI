import { useMutation, useQueryClient } from '@tanstack/react-query';

import { createAppointmentApi } from '../api/appointment-booking.api';

export function useCreateAppointmentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: createAppointmentApi,
    onSuccess: (created) => {
      queryClient.setQueryData(['appointments', 'detail', created.id], created);
      queryClient.setQueryData(['appointments', 'detail', created.appointmentCode], created);
      queryClient.invalidateQueries({ queryKey: ['appointments'] });
    },
  });
}
