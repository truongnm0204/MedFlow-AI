import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { callNextDoctorPatient, fetchDoctorAssignedQueue } from '../api/doctor-assigned-queue.api';
import type {
  DoctorAssignedQueueQueryParams,
  DoctorCallNextRequestPayload,
  DoctorCallNextResponseDto,
} from '../types/doctor-assigned-queue.types';

export function useDoctorAssignedQueue(params?: DoctorAssignedQueueQueryParams) {
  return useQuery({
    queryKey: ['doctor', 'assigned-queue', params ?? null],
    queryFn: () => fetchDoctorAssignedQueue(params),
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
}

export interface CallNextDoctorPatientVariables {
  servicePointId: string;
  payload?: DoctorCallNextRequestPayload;
}

export function useCallNextDoctorPatient() {
  const queryClient = useQueryClient();

  return useMutation<DoctorCallNextResponseDto, unknown, CallNextDoctorPatientVariables>({
    mutationFn: ({ servicePointId, payload }) => callNextDoctorPatient(servicePointId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'assigned-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'dashboard'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
    },
  });
}
