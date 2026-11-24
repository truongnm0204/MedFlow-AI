import { useQuery } from '@tanstack/react-query';

import { fetchMyVisitsApi } from '../api/payment.api';

export function useMyVisitsQuery() {
  return useQuery({
    queryKey: ['my-visits'],
    queryFn: () => fetchMyVisitsApi(20),
    retry: false,
    staleTime: 15_000,
  });
}
