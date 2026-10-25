import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { createFamilyMember } from '../api/family.api';
import type { CreateFamilyMemberInput } from '../schemas/family-member.schema';
import type { FamilyMember } from '../types/family.types';

export function useCreateFamilyMember(): UseMutationResult<
  FamilyMember,
  Error,
  CreateFamilyMemberInput
> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateFamilyMemberInput) => createFamilyMember(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['family-members'] });
      void queryClient.invalidateQueries({ queryKey: ['family-member-detail'] });
    },
  });
}

