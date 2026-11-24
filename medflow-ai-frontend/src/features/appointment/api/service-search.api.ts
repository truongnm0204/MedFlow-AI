import { apiClient } from '@/shared/lib/axios';

import type {
  ExaminationService,
  ServiceSearchParams,
  ServiceSearchResponse,
} from '../types/service-search.types';

export async function searchExaminationServicesApi(
  params: ServiceSearchParams = {},
): Promise<ServiceSearchResponse> {
  const { data } = await apiClient.get<ServiceSearchResponse>('/examination-services', {
    params: {
      query: params.query?.trim() || undefined,
      category: params.category ?? 'all',
      department: params.department ?? 'all',
      priceDurationRange: params.priceDurationRange ?? 'all',
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 6,
    },
  });
  return data;
}

export async function getExaminationServiceDetailApi(
  serviceId: string,
): Promise<ExaminationService | null> {
  const normalized = serviceId.trim();
  if (!normalized) return null;

  const { data } = await apiClient.get<ExaminationService>(
    `/examination-services/${encodeURIComponent(normalized)}`,
  );
  return data;
}
