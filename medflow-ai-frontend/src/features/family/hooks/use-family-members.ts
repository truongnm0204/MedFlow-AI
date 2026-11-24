import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchFamilyMembers } from '../api/family.api';
import type { FamilyMember } from '../types/family.types';

export const FAMILY_MEMBERS_QUERY_KEY = ['family-members'] as const;

export function useFamilyMembers(): UseQueryResult<FamilyMember[], Error> {
  return useQuery<FamilyMember[], Error>({
    queryKey: FAMILY_MEMBERS_QUERY_KEY,
    queryFn: fetchFamilyMembers,
    retry: false,
  });
}
