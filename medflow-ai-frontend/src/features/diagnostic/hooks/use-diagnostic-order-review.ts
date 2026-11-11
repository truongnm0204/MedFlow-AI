import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  confirmDiagnosticOrder,
  confirmDiagnosticOrdersBatch,
  fetchDiagnosticOrderReviewDetail,
  fetchDiagnosticOrdersForReview,
} from '../api/diagnostic.api';
import type {
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrdersBatchRequest,
  GetDiagnosticOrdersForReviewParams,
} from '../types/diagnostic-order-review.types';

export function useDiagnosticOrdersForReview(
  params?: GetDiagnosticOrdersForReviewParams,
  enabled = true,
) {
  return useQuery({
    queryKey: ['diagnostic', 'orders', 'review', params ?? {}],
    queryFn: () => fetchDiagnosticOrdersForReview(params),
    enabled,
    staleTime: 30_000,
  });
}

export function useDiagnosticOrderReviewDetail(idOrCode?: string | null, enabled = true) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['diagnostic', 'orders', 'review', 'detail', normalized],
    queryFn: () => fetchDiagnosticOrderReviewDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 30_000,
  });
}

export function useConfirmDiagnosticOrder() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: ConfirmDiagnosticOrderRequest;
    }) => confirmDiagnosticOrder(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'review'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useConfirmDiagnosticOrdersBatch() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: ConfirmDiagnosticOrdersBatchRequest) =>
      confirmDiagnosticOrdersBatch(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'review'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}
