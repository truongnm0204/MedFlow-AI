import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { fetchExaminationQueue, updateExaminationQueueStatus } from '../api/examination-queue.api';
import { applyQueueStatusUpdateToResponse } from '../data/examination-queue-mock';
import type {
  ExaminationQueueQueryParams,
  ExaminationQueueResponseDto,
  UpdateExaminationQueueStatusPayload,
  UpdateExaminationQueueStatusResponseDto,
} from '../types/examination-queue.types';
export function useExaminationQueue(params?: ExaminationQueueQueryParams, enabled = true) {
  return useQuery({
    queryKey: ['reception', 'examination-queue', params ?? null],
    queryFn: () => fetchExaminationQueue(params),
    enabled,
    staleTime: 30_000,
  });
}

export interface UpdateExaminationQueueStatusVariables {
  queueEntryId: string;
  payload: UpdateExaminationQueueStatusPayload;
  currentQueueData?: ExaminationQueueResponseDto;
}

export function useUpdateExaminationQueueStatus() {
  const queryClient = useQueryClient();

  return useMutation<
    UpdateExaminationQueueStatusResponseDto,
    unknown,
    UpdateExaminationQueueStatusVariables
  >({
    mutationFn: ({ queueEntryId, payload, currentQueueData }) =>
      updateExaminationQueueStatus(queueEntryId, payload, currentQueueData),
    onSuccess: (result) => {
      queryClient.setQueriesData<ExaminationQueueResponseDto>(
        { queryKey: ['reception', 'examination-queue'] },
        (old) => (old ? applyQueueStatusUpdateToResponse(old, result.entry) : old),
      );
    },
  });
}
