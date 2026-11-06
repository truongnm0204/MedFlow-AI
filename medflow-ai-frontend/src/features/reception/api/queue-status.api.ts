import { apiClient } from '@/shared/lib/axios';

import type {
  QueueEntryStatusDetailDto,
  UpdateQueuePriorityRequestPayload,
  UpdateQueuePriorityResponseDto,
  UpdateQueueStatusRequestPayload,
  UpdateQueueStatusResponseDto,
} from '../types/queue-status-update.types';

export async function fetchQueueEntryStatus(idOrCode: string): Promise<QueueEntryStatusDetailDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<QueueEntryStatusDetailDto>(
    `/reception/examination-queue/entries/${encoded}/status`,
  );
  return data;
}

export async function updateQueueEntryStatus(
  idOrCode: string,
  payload: UpdateQueueStatusRequestPayload,
): Promise<UpdateQueueStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.patch<UpdateQueueStatusResponseDto>(
    `/reception/examination-queue/entries/${encoded}/status`,
    payload,
  );
  return data;
}

export async function updateQueueEntryPriority(
  idOrCode: string,
  payload: UpdateQueuePriorityRequestPayload,
): Promise<UpdateQueuePriorityResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.patch<UpdateQueuePriorityResponseDto>(
    `/reception/examination-queue/entries/${encoded}/priority`,
    payload,
  );
  return data;
}
