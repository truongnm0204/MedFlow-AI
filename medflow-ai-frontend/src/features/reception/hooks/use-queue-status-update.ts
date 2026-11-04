import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { fetchQueueEntryStatus, updateQueueEntryStatus } from '../api/queue-status.api';
import type {
  QueueEntryStatusDetailDto,
  UpdateQueueStatusRequestPayload,
  UpdateQueueStatusResponseDto,
} from '../types/queue-status-update.types';

export interface UpdateQueueStatusVariables {
  idOrCode: string;
  payload: UpdateQueueStatusRequestPayload;
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
