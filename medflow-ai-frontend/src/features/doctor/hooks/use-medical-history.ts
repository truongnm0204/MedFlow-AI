import { useQuery } from '@tanstack/react-query';

import { fetchPatientMedicalHistory, fetchVisitMedicalHistory } from '../api/medical-history.api';
import type {
  GetPatientMedicalHistoryQueryParams,
  MedicalHistoryLookupMode,
} from '../types/medical-history.types';

export interface UseMedicalHistoryOptions {
  mode: MedicalHistoryLookupMode;
  identifier: string;
  params?: GetPatientMedicalHistoryQueryParams;
  enabled?: boolean;
}

export function useMedicalHistory({
  mode,
  identifier,
  params,
  enabled = true,
}: UseMedicalHistoryOptions) {
  const trimmedIdentifier = identifier.trim();

  return useQuery({
    queryKey: ['doctor', 'medical-history', mode, trimmedIdentifier, params ?? null],
    queryFn: () =>
      mode === 'visit'
        ? fetchVisitMedicalHistory(trimmedIdentifier, params)
        : fetchPatientMedicalHistory(trimmedIdentifier, params),
    enabled: enabled && trimmedIdentifier.length > 0,
    staleTime: 15_000,
  });
}
