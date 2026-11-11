import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  enterDiagnosticResult,
  fetchDiagnosticOrdersForResultEntry,
  fetchDiagnosticResultEntryDetail,
} from '../api/diagnostic.api';
import type {
  EnterDiagnosticResultRequest,
  GetDiagnosticOrdersForResultEntryParams,
} from '../types/diagnostic-result-entry.types';

export function useDiagnosticOrdersForResultEntry(
  params?: GetDiagnosticOrdersForResultEntryParams,
  enabled = true,
) {
  return useQuery({
    queryKey: ['diagnostic', 'results', 'entry', params ?? {}],
    queryFn: () => fetchDiagnosticOrdersForResultEntry(params),
    enabled,
    staleTime: 30_000,
  });
}

export function useDiagnosticResultEntryDetail(idOrCode?: string | null, enabled = true) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['diagnostic', 'results', 'entry', 'detail', normalized],
    queryFn: () => fetchDiagnosticResultEntryDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 30_000,
  });
}

export function useEnterDiagnosticResult() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: EnterDiagnosticResultRequest;
    }) => enterDiagnosticResult(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'results'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'queue'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders'] });
    },
  });
}
