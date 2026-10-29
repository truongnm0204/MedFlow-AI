import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { searchExaminationServicesApi } from '../api/service-search.api';
import type { ServiceSearchParams, ServiceSearchResponse } from '../types/service-search.types';

export function useExaminationServicesQuery(
  params: ServiceSearchParams,
): UseQueryResult<ServiceSearchResponse, Error> {
  return useQuery<ServiceSearchResponse, Error>({
    queryKey: ['examination-services', params],
    queryFn: () => searchExaminationServicesApi(params),
    placeholderData: (prev) => prev,
  });
}
