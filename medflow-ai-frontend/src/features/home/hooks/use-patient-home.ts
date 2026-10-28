import { useMutation, useQuery } from '@tanstack/react-query';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { fetchPatientHomeSummary, searchQuickSlots } from '../api/home.api';
import type { QuickSlotSearchRequest } from '../types/home.types';

export function usePatientHomeSummary(enabledOverride?: boolean) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const enabled = enabledOverride ?? isAuthenticated;

  return useQuery({
    queryKey: ['patient-home', 'summary'],
    queryFn: fetchPatientHomeSummary,
    enabled,
    staleTime: 60_000,
  });
}

export function useQuickSlotSearch() {
  return useMutation({
    mutationFn: (payload: QuickSlotSearchRequest) => searchQuickSlots(payload),
  });
}
