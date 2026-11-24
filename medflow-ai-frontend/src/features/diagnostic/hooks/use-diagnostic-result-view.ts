import { useQuery } from '@tanstack/react-query';

import {
  fetchDiagnosticResultViewDetail,
  fetchDiagnosticResultViewsList,
} from '../api/diagnostic-result-view.api';

export function useDiagnosticResultViewDetail(orderIdOrCode: string | undefined) {
  const code = orderIdOrCode?.trim() || '';
  return useQuery({
    queryKey: ['diagnostic', 'result-view', code] as const,
    queryFn: () => fetchDiagnosticResultViewDetail(code),
    enabled: code.length > 0,
    retry: false,
  });
}

export function useDiagnosticResultViewsList(enabled = true) {
  return useQuery({
    queryKey: ['diagnostic', 'result-views-list'] as const,
    queryFn: fetchDiagnosticResultViewsList,
    enabled,
    retry: false,
  });
}
