import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import { callNextDoctorPatient, fetchDoctorAssignedQueue } from '../api/doctor-assigned-queue.api';
import type {
  DoctorAssignedQueueQueryParams,
  DoctorCallNextRequestPayload,
  DoctorCallNextResponseDto,
} from '../types/doctor-assigned-queue.types';

const HOSPITAL_QUEUE_CHANNEL: RealtimeChannel = { type: 'hospital-queue' };
const DOCTOR_QUEUE_CONNECTED_POLL_MS = 30_000;
const DOCTOR_QUEUE_FALLBACK_POLL_MS = 15_000;

export function useDoctorAssignedQueue(params?: DoctorAssignedQueueQueryParams) {
  const queryClient = useQueryClient();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const handleRealtimeQueueChanged = useCallback(() => {
    scheduleCoalescedInvalidation(queryClient, ['doctor', 'assigned-queue']);
    scheduleCoalescedInvalidation(queryClient, ['doctor', 'dashboard']);
  }, [queryClient]);

  const { isLive } = useRealtimeSubscription({
    channel: HOSPITAL_QUEUE_CHANNEL,
    event: 'QueueChanged',
    onEvent: handleRealtimeQueueChanged,
    onReconnected: handleRealtimeQueueChanged,
    enabled: isAuthenticated,
  });

  return useQuery({
    queryKey: ['doctor', 'assigned-queue', params ?? null],
    queryFn: () => fetchDoctorAssignedQueue(params),
    staleTime: 15_000,
    refetchInterval: isLive ? DOCTOR_QUEUE_CONNECTED_POLL_MS : DOCTOR_QUEUE_FALLBACK_POLL_MS,
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
