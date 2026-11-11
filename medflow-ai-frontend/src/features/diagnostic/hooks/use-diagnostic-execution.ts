import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  completeDiagnosticServiceExecution,
  fetchDiagnosticExecutionDetail,
  fetchDiagnosticExecutions,
  startDiagnosticServiceExecution,
  updateDiagnosticServiceStatus,
  updateDiagnosticServiceStatusBatch,
} from '../api/diagnostic.api';
import type {
  GetDiagnosticExecutionsParams,
  UpdateDiagnosticServiceStatusBatchRequest,
  UpdateDiagnosticServiceStatusRequest,
} from '../types/diagnostic-execution.types';

function useInvalidateExecutionQueries() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'execution'] });
    void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
    void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'queue'] });
    void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'receive'] });
    void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
  };
}

export function useDiagnosticExecutions(params?: GetDiagnosticExecutionsParams, enabled = true) {
  return useQuery({
    queryKey: ['diagnostic', 'execution', 'list', params ?? {}],
    queryFn: () => fetchDiagnosticExecutions(params),
    enabled,
    staleTime: 30_000,
  });
}

export function useDiagnosticExecutionDetail(idOrCode?: string | null, enabled = true) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['diagnostic', 'execution', 'detail', normalized],
    queryFn: () => fetchDiagnosticExecutionDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 30_000,
  });
}

export function useUpdateDiagnosticServiceStatus() {
  const invalidate = useInvalidateExecutionQueries();
  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: UpdateDiagnosticServiceStatusRequest;
    }) => updateDiagnosticServiceStatus(idOrCode, payload),
    onSuccess: invalidate,
  });
}

export function useStartDiagnosticServiceExecution() {
  const invalidate = useInvalidateExecutionQueries();
  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: UpdateDiagnosticServiceStatusRequest;
    }) => startDiagnosticServiceExecution(idOrCode, payload),
    onSuccess: invalidate,
  });
}

export function useCompleteDiagnosticServiceExecution() {
  const invalidate = useInvalidateExecutionQueries();
  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: UpdateDiagnosticServiceStatusRequest;
    }) => completeDiagnosticServiceExecution(idOrCode, payload),
    onSuccess: invalidate,
  });
}

export function useUpdateDiagnosticServiceStatusBatch() {
  const invalidate = useInvalidateExecutionQueries();
  return useMutation({
    mutationFn: (payload: UpdateDiagnosticServiceStatusBatchRequest) =>
      updateDiagnosticServiceStatusBatch(payload),
    onSuccess: invalidate,
  });
}
