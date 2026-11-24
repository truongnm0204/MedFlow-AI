import { useQuery } from '@tanstack/react-query';

import {
  fetchPatientDiagnosticResultDetail,
  fetchPatientDiagnosticResults,
} from '../api/patient-diagnostic-result.api';

export function usePatientDiagnosticResultDetail(orderIdOrCode: string | undefined) {
  const trimmed = orderIdOrCode?.trim() ?? '';

  return useQuery({
    queryKey: ['patient-diagnostic-result', trimmed] as const,
    queryFn: () => fetchPatientDiagnosticResultDetail(trimmed),
    enabled: trimmed.length > 0,
    retry: false,
  });
}

export function usePatientDiagnosticResultsList(enabled = true) {
  return useQuery({
    queryKey: ['patient-diagnostic-results'] as const,
    queryFn: fetchPatientDiagnosticResults,
    enabled,
    retry: false,
  });
}
