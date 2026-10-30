import { useQuery } from '@tanstack/react-query';
import { useAuthStore } from '@/features/auth/store/auth.store';

import { getMyPatientProfileApi } from '../api/patient-profile.api';
import { mockPatientProfile } from '../data/patient-profile.mock';

export const PATIENT_PROFILE_QUERY_KEY = ['patient-profile', 'me'] as const;

export function usePatientProfileQuery() {
  const userId = useAuthStore((s) => s.user?.id ?? null);

  return useQuery({
    queryKey: [...PATIENT_PROFILE_QUERY_KEY, userId] as const,
    queryFn: getMyPatientProfileApi,
    initialData: mockPatientProfile,
    initialDataUpdatedAt: 0,
    staleTime: 60_000,
    retry: false,
  });
}
