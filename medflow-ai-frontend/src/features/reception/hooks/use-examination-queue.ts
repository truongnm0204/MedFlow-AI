import { useQuery } from '@tanstack/react-query';

import { fetchExaminationQueue } from '../api/examination-queue.api';
import type { ExaminationQueueQueryParams } from '../types/examination-queue.types';

export function useExaminationQueue(params?: ExaminationQueueQueryParams, enabled = true) {
  return useQuery({
    queryKey: ['reception', 'examination-queue', params ?? null],
    queryFn: () => fetchExaminationQueue(params),
    enabled,
    staleTime: 30_000,
  });
}
