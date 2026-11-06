import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';

import { fetchQueuePolicyApi, updateQueuePolicyApi } from '../api/queue-policy.api';
import type { QueuePolicyDto, UpdateQueuePolicyRequest } from '../types/queue-policy.types';

export const QUEUE_POLICY_QUERY_KEY = ['admin', 'queue-policy'] as const;

export function useQueuePolicy(): UseQueryResult<QueuePolicyDto, Error> {
  return useQuery({
    queryKey: QUEUE_POLICY_QUERY_KEY,
    queryFn: fetchQueuePolicyApi,
    retry: false,
  });
}

export function useUpdateQueuePolicy(): UseMutationResult<
  QueuePolicyDto,
  Error,
  UpdateQueuePolicyRequest
> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload) => updateQueuePolicyApi(payload),
    onSuccess: (updated) => {
      queryClient.setQueryData(QUEUE_POLICY_QUERY_KEY, updated);
    },
  });
}
