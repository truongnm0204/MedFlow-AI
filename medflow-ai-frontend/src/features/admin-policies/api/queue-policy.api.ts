import { apiClient } from '@/shared/lib/axios';

import type { QueuePolicyDto, UpdateQueuePolicyRequest } from '../types/queue-policy.types';

export async function fetchQueuePolicyApi(): Promise<QueuePolicyDto> {
  const { data } = await apiClient.get<QueuePolicyDto>('/admin/queue-policy');
  return data;
}

export async function updateQueuePolicyApi(
  payload: UpdateQueuePolicyRequest,
): Promise<QueuePolicyDto> {
  const { data } = await apiClient.put<QueuePolicyDto>('/admin/queue-policy', payload);
  return data;
}
