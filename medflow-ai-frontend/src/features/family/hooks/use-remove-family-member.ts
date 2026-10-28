import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { removeFamilyMember } from '../api/family.api';

export interface RemoveFamilyMemberVariables {
  memberId: string;
  reason?: string;
}

export function useRemoveFamilyMember(): UseMutationResult<
  void,
  Error,
  RemoveFamilyMemberVariables
> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ memberId, reason }: RemoveFamilyMemberVariables) =>
      removeFamilyMember(memberId, reason),
    onSuccess: (_, { memberId }) => {
      void queryClient.invalidateQueries({ queryKey: ['family-members'] });
      void queryClient.invalidateQueries({ queryKey: ['family-member-detail', memberId] });
      void queryClient.invalidateQueries({ queryKey: ['family-member-detail'] });
    },
  });
}
