import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { fetchDoctorDiagnosticTracking } from '../api/doctor-diagnostic-tracking.api';
import type { DoctorDiagnosticTrackingQueryParams } from '../types/doctor-diagnostic-tracking.types';

export function useDoctorDiagnosticTracking(params: DoctorDiagnosticTrackingQueryParams) {
  return useQuery({
    queryKey: ['doctor', 'diagnostic-results', 'tracking', params],
    queryFn: () => fetchDoctorDiagnosticTracking(params),
    staleTime: 15_000,
    refetchInterval: 30_000,
    placeholderData: keepPreviousData,
  });
}
