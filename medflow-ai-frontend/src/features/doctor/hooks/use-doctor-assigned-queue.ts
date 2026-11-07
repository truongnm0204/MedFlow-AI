import { useQuery } from '@tanstack/react-query';

import { fetchDoctorAssignedQueue } from '../api/doctor-assigned-queue.api';
import type { DoctorAssignedQueueQueryParams } from '../types/doctor-assigned-queue.types';

export function useDoctorAssignedQueue(params?: DoctorAssignedQueueQueryParams) {
  return useQuery({
    queryKey: ['doctor', 'assigned-queue', params ?? null],
    queryFn: () => fetchDoctorAssignedQueue(params),
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
}
