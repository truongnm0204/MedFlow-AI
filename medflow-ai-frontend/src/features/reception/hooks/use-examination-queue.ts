import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { applyEarlyArrivalBackfill, fetchExaminationQueue } from '../api/examination-queue.api';
import type {
  EarlyArrivalBackfillRequestPayload,
  ExaminationQueueQueryParams,
} from '../types/examination-queue.types';
export function useExaminationQueue(params?: ExaminationQueueQueryParams, enabled = true) {
  return useQuery({
    queryKey: ['reception', 'examination-queue', params ?? null],
    queryFn: () => fetchExaminationQueue(params),
    enabled,
    staleTime: 30_000,
  });
}

export interface ApplyEarlyArrivalBackfillVariables {
  queueEntryId: string;
  payload: EarlyArrivalBackfillRequestPayload;
}

export function useApplyEarlyArrivalBackfill() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ queueEntryId, payload }: ApplyEarlyArrivalBackfillVariables) =>
      applyEarlyArrivalBackfill(queueEntryId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
    },
  });
}
