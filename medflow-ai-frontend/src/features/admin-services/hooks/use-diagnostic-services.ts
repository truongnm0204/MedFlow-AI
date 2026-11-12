import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { fetchDiagnosticServicesApi } from '../api/diagnostic-service.api';
import type {
  DiagnosticServiceDepartmentOption,
  DiagnosticServiceListParams,
} from '../types/diagnostic-service.types';

export function useDiagnosticServiceList(params: DiagnosticServiceListParams) {
  return useQuery({
    queryKey: ['admin-diagnostic-services', 'list', params],
    queryFn: () => fetchDiagnosticServicesApi(params),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });
}

export function useDiagnosticServiceDepartmentOptions() {
  return useQuery({
    queryKey: ['admin-diagnostic-services', 'department-options'],
    queryFn: () => fetchDiagnosticServicesApi({ page: 1, pageSize: 100 }),
    staleTime: 300_000,
    select: (response): DiagnosticServiceDepartmentOption[] => {
      const map = new Map<number, string>();
      for (const item of response.data) {
        if (item.departmentId !== null && item.departmentName !== null) {
          if (!map.has(item.departmentId)) {
            map.set(item.departmentId, item.departmentName);
          }
        }
      }
      return Array.from(map.entries())
        .map(([id, name]) => ({ id, name }))
        .sort((a, b) => a.name.localeCompare(b.name, 'vi'));
    },
  });
}
