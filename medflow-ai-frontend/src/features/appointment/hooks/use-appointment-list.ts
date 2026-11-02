import { useQuery } from '@tanstack/react-query';

import { fetchPatientAppointmentsApi } from '../api/appointment-list.api';
import type { AppointmentListFilterParams } from '../types/appointment-list.types';

export function useAppointmentListQuery(params: AppointmentListFilterParams) {
  return useQuery({
    queryKey: ['appointments', 'list', params],
    queryFn: () => fetchPatientAppointmentsApi(params),
  });
}
