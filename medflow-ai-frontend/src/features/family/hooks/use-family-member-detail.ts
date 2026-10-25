import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchFamilyMemberById } from '../api/family.api';
import type { FamilyMember } from '../types/family.types';

export function useFamilyMemberDetail(memberId: string): UseQueryResult<FamilyMember, Error> {
  return useQuery<FamilyMember, Error>({
    queryKey: ['family-member-detail', memberId],
    queryFn: () => fetchFamilyMemberById(memberId),
    enabled: Boolean(memberId),
  });
}

