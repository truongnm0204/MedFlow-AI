import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { getMyPatientProfileApi, updateMyPatientProfileApi } from '../api/patient-profile.api';
import type { PatientProfile, UpdateMyPatientProfileInput } from '../types/patient-profile.types';

export const PATIENT_PROFILE_QUERY_KEY = ['patient-profile', 'me'] as const;

export function usePatientProfileQuery() {
  const userId = useAuthStore((s) => s.user?.id ?? null);

  return useQuery({
    queryKey: [...PATIENT_PROFILE_QUERY_KEY, userId] as const,
    queryFn: getMyPatientProfileApi,
    staleTime: 60_000,
    retry: false,
  });
}

export function useUpdatePatientProfileMutation() {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id ?? null);

  return useMutation<PatientProfile, unknown, UpdateMyPatientProfileInput>({
    mutationFn: updateMyPatientProfileApi,
    onSuccess: (updatedProfile) => {
      queryClient.setQueryData([...PATIENT_PROFILE_QUERY_KEY, userId] as const, updatedProfile);
      void queryClient.invalidateQueries({ queryKey: PATIENT_PROFILE_QUERY_KEY });
    },
  });
}
