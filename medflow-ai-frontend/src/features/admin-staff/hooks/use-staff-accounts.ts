import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createAdminStaffAccountApi,
  deactivateAdminStaffAccountApi,
  fetchAdminStaffAccountsApi,
  updateAdminStaffAccountApi,
} from '../api/staff-account.api';
import type { UpdateStaffAccountInput } from '../types/staff-update.types';

export const adminStaffAccountsQueryKey = ['admin', 'staff-accounts'] as const;

export function useAdminStaffAccounts() {
  return useQuery({
    queryKey: adminStaffAccountsQueryKey,
    queryFn: fetchAdminStaffAccountsApi,
  });
}

export function useCreateStaffAccountMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createAdminStaffAccountApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminStaffAccountsQueryKey });
    },
  });
}

export function useUpdateStaffAccountMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      staffIdentifier,
      values,
    }: {
      staffIdentifier: string;
      values: UpdateStaffAccountInput;
    }) => updateAdminStaffAccountApi(staffIdentifier, values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminStaffAccountsQueryKey });
    },
  });
}

export function useDeactivateStaffAccountMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deactivateAdminStaffAccountApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminStaffAccountsQueryKey });
    },
  });
}
