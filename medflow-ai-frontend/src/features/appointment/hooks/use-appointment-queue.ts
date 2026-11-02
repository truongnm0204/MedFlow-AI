import { useQuery } from '@tanstack/react-query';

import { fetchAppointmentQueueStatusApi } from '../api/appointment-queue.api';

export function useAppointmentQueueStatusQuery(appointmentId: string) {
  return useQuery({
    queryKey: ['appointments', 'queue', appointmentId],
    queryFn: () => fetchAppointmentQueueStatusApi(appointmentId),
    enabled: Boolean(appointmentId.trim()),
    retry: false,
    refetchInterval: 10_000,
  });
}
