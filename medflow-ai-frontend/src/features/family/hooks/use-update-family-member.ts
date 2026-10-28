import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { updateFamilyMember } from '../api/family.api';
import type { UpdateFamilyMemberInput } from '../schemas/family-member.schema';
import type { FamilyMember } from '../types/family.types';

export interface UpdateFamilyMemberVariables {
  memberId: string;
  payload: UpdateFamilyMemberInput;
}

export function useUpdateFamilyMember(): UseMutationResult<
  FamilyMember,
  Error,
  UpdateFamilyMemberVariables
> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ memberId, payload }: UpdateFamilyMemberVariables) =>
      updateFamilyMember(memberId, payload),
    onSuccess: (updated) => {
      void queryClient.invalidateQueries({ queryKey: ['family-members'] });
      void queryClient.invalidateQueries({ queryKey: ['family-member-detail', updated.id] });
      void queryClient.invalidateQueries({ queryKey: ['family-member-detail'] });
    },
  });
}
