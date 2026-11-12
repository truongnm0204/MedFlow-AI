import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_SERVICES_MOCK } from '../data/diagnostic-services-data';
import type {
  DiagnosticServiceListParams,
  DiagnosticServiceListResponse,
} from '../types/diagnostic-service.types';

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

function queryDiagnosticServicesMock(
  params: DiagnosticServiceListParams,
): DiagnosticServiceListResponse {
  const normalizedKeyword = params.keyword?.trim().toLowerCase() ?? '';

  const filtered = DIAGNOSTIC_SERVICES_MOCK.data.filter((item) => {
    if (normalizedKeyword) {
      const matchesCode = item.serviceCode.toLowerCase().includes(normalizedKeyword);
      const matchesName = item.name.toLowerCase().includes(normalizedKeyword);
      const matchesDesc = Boolean(
        item.description && item.description.toLowerCase().includes(normalizedKeyword),
      );
      if (!matchesCode && !matchesName && !matchesDesc) {
        return false;
      }
    }

    if (params.departmentId !== undefined && item.departmentId !== params.departmentId) {
      return false;
    }

    if (params.status !== undefined && item.status !== params.status) {
      return false;
    }

    if (
      params.requiresResultVerification !== undefined &&
      item.requiresResultVerification !== params.requiresResultVerification
    ) {
      return false;
    }

    return true;
  });

  const sorted = [...filtered].sort((a, b) => a.id - b.id);
  const total = sorted.length;
  const page = Math.max(1, params.page);
  const pageSize = Math.max(1, params.pageSize);
  const totalPages = total > 0 ? Math.ceil(total / pageSize) : 0;
  const start = (page - 1) * pageSize;
  const data = sorted.slice(start, start + pageSize);

  return {
    ...DIAGNOSTIC_SERVICES_MOCK,
    data,
    total,
    page,
    pageSize,
    totalPages,
  };
}

export async function fetchDiagnosticServicesApi(
  params: DiagnosticServiceListParams,
): Promise<DiagnosticServiceListResponse> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<DiagnosticServiceListResponse>(
        '/admin/diagnostic-services',
        { params },
      );
      return data;
    } catch (error) {
      // Only fallback to mock on network/offline errors; rethrow all HTTP 4xx/5xx responses.
      if (!axios.isAxiosError(error) || error.response) {
        throw error;
      }
    }
  }

  return queryDiagnosticServicesMock(params);
}
