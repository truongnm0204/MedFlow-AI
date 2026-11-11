import { useQuery } from '@tanstack/react-query';

import { fetchDiagnosticWorklist, fetchDiagnosticWorklistOrderDetail } from '../api/diagnostic.api';
import type { DiagnosticWorklistQueryParams } from '../types/diagnostic-worklist.types';

export function useDiagnosticWorklist(params?: DiagnosticWorklistQueryParams, enabled = true) {
  return useQuery({
    queryKey: ['diagnostic', 'worklist', params ?? null],
    queryFn: () => fetchDiagnosticWorklist(params),
    enabled,
    staleTime: 15_000,
  });
}

export function useDiagnosticWorklistOrderDetail(orderIdOrCode: string | null, enabled = true) {
  const normalized = orderIdOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['diagnostic', 'worklist', 'detail', normalized || null],
    queryFn: () => fetchDiagnosticWorklistOrderDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 15_000,
  });
}
