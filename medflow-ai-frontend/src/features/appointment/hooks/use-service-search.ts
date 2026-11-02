import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import {
  getExaminationServiceDetailApi,
  searchExaminationServicesApi,
} from '../api/service-search.api';
import type {
  ExaminationService,
  ServiceSearchParams,
  ServiceSearchResponse,
} from '../types/service-search.types';

export function useExaminationServicesQuery(
  params: ServiceSearchParams,
): UseQueryResult<ServiceSearchResponse, Error> {
  return useQuery<ServiceSearchResponse, Error>({
    queryKey: ['examination-services', params],
    queryFn: () => searchExaminationServicesApi(params),
    placeholderData: (prev) => prev,
  });
}

export function useExaminationServiceDetailQuery(
  serviceId?: string | null,
): UseQueryResult<ExaminationService | null, Error> {
  return useQuery<ExaminationService | null, Error>({
    queryKey: ['examination-service-detail', serviceId],
    queryFn: () => getExaminationServiceDetailApi(serviceId!),
    enabled: Boolean(serviceId?.trim()),
  });
}
