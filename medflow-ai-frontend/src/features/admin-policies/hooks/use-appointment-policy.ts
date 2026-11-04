import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';

import {
  fetchAppointmentPolicyApi,
  updateAppointmentPolicyApi,
} from '../api/appointment-policy.api';
import type {
  AppointmentPolicyDto,
  UpdateAppointmentPolicyRequest,
} from '../types/appointment-policy.types';

export const APPOINTMENT_POLICY_QUERY_KEY = ['admin', 'appointment-policy'] as const;

export function useAppointmentPolicy(): UseQueryResult<AppointmentPolicyDto, Error> {
  return useQuery({
    queryKey: APPOINTMENT_POLICY_QUERY_KEY,
    queryFn: fetchAppointmentPolicyApi,
    retry: false,
  });
}

export function useUpdateAppointmentPolicy(): UseMutationResult<
  AppointmentPolicyDto,
  Error,
  UpdateAppointmentPolicyRequest
> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload) => updateAppointmentPolicyApi(payload),
    onSuccess: (updated) => {
      queryClient.setQueryData(APPOINTMENT_POLICY_QUERY_KEY, updated);
    },
  });
}
