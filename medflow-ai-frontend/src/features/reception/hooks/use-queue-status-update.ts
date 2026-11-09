import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  cancelQueueEntry,
  fetchQueueEntryStatus,
  updateQueueEntryPriority,
  updateQueueEntryStatus,
} from '../api/queue-status.api';
import type {
  CancelQueueEntryRequestPayload,
  CancelQueueEntryResponseDto,
  QueueEntryStatusDetailDto,
  UpdateQueuePriorityRequestPayload,
  UpdateQueuePriorityResponseDto,
  UpdateQueueStatusRequestPayload,
  UpdateQueueStatusResponseDto,
} from '../types/queue-status-update.types';

export interface UpdateQueueStatusVariables {
  idOrCode: string;
  payload: UpdateQueueStatusRequestPayload;
}

export interface UpdateQueuePriorityVariables {
  idOrCode: string;
  payload: UpdateQueuePriorityRequestPayload;
}

export interface CancelQueueEntryVariables {
  idOrCode: string;
  payload: CancelQueueEntryRequestPayload;
}

export function useQueueEntryStatusDetail(idOrCode: string | null, enabled = true) {
  const trimmed = idOrCode?.trim() ?? '';
  return useQuery<QueueEntryStatusDetailDto>({
    queryKey: ['queue-entry-status', trimmed],
    queryFn: () => fetchQueueEntryStatus(trimmed),
    enabled: enabled && trimmed.length > 0,
    retry: false,
  });
}

export function useUpdateQueueStatus() {
  const queryClient = useQueryClient();

  return useMutation<UpdateQueueStatusResponseDto, unknown, UpdateQueueStatusVariables>({
    mutationFn: ({ idOrCode, payload }) => updateQueueEntryStatus(idOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
    },
  });
}

export function useUpdateQueuePriority() {
  const queryClient = useQueryClient();

  return useMutation<UpdateQueuePriorityResponseDto, unknown, UpdateQueuePriorityVariables>({
    mutationFn: ({ idOrCode, payload }) => updateQueueEntryPriority(idOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
    },
  });
}

export function useCancelQueueEntry() {
  const queryClient = useQueryClient();

  return useMutation<CancelQueueEntryResponseDto, unknown, CancelQueueEntryVariables>({
    mutationFn: ({ idOrCode, payload }) => cancelQueueEntry(idOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      // Lượt đã hủy là trạng thái cuối: bỏ cache chi tiết thay vì refetch.
      queryClient.removeQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
    },
  });
}
