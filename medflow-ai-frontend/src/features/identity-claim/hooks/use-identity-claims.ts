import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  approveIdentityClaimApi,
  createIdentityClaimApi,
  fetchIdentityClaimApi,
  fetchIdentityClaimQueueApi,
  fetchIdentityHospitalsApi,
  fetchMyIdentityClaimsApi,
  rejectIdentityClaimApi,
  sendIdentityClaimOtpApi,
} from '../api/identity-claim.api';
import type { CreateIdentityClaimPayload } from '../types/identity-claim.types';

export function useIdentityHospitalsQuery() {
  return useQuery({
    queryKey: ['identity-claim-hospitals'],
    queryFn: fetchIdentityHospitalsApi,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
}

export function useMyIdentityClaimsQuery() {
  return useQuery({
    queryKey: ['identity-claims'],
    queryFn: fetchMyIdentityClaimsApi,
    retry: false,
  });
}

export function useIdentityClaimQuery(claimId?: number) {
  return useQuery({
    queryKey: ['identity-claim', claimId],
    queryFn: () => fetchIdentityClaimApi(claimId!),
    enabled: typeof claimId === 'number' && claimId > 0,
    retry: false,
    refetchInterval: (query) => (query.state.data?.status === 'PENDING' ? 10_000 : false),
  });
}

export function useSendIdentityClaimOtpMutation() {
  return useMutation({ mutationFn: sendIdentityClaimOtpApi });
}

export function useCreateIdentityClaimMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateIdentityClaimPayload) => createIdentityClaimApi(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['identity-claims'] });
    },
  });
}

export function useIdentityClaimQueueQuery(status?: string) {
  return useQuery({
    queryKey: ['identity-claim-queue', status ?? 'all'],
    queryFn: () => fetchIdentityClaimQueueApi(status),
    retry: false,
    refetchInterval: 15_000,
  });
}

export function useApproveIdentityClaimMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: approveIdentityClaimApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['identity-claim-queue'] });
    },
  });
}

export function useRejectIdentityClaimMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ claimId, reason }: { claimId: number; reason: string }) =>
      rejectIdentityClaimApi(claimId, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['identity-claim-queue'] });
    },
  });
}
