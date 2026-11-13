import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createDiagnosticServiceApi,
  deactivateDiagnosticServiceApi,
  fetchDiagnosticServiceFormOptionsApi,
  fetchDiagnosticServicesApi,
  updateDiagnosticServiceApi,
} from '../api/diagnostic-service.api';
import type {
  CreateDiagnosticServicePayload,
  DeactivateDiagnosticServicePayload,
  DiagnosticServiceDepartmentOption,
  DiagnosticServiceListParams,
  UpdateDiagnosticServicePayload,
} from '../types/diagnostic-service.types';

function useInvalidateDiagnosticServices() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-diagnostic-services'] });
  };
}

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

export function useDiagnosticServiceFormOptions(enabled: boolean) {
  return useQuery({
    queryKey: ['admin-diagnostic-services', 'form-options'],
    queryFn: fetchDiagnosticServiceFormOptionsApi,
    staleTime: 300_000,
    enabled,
  });
}

export function useCreateDiagnosticService() {
  const invalidate = useInvalidateDiagnosticServices();
  return useMutation({
    mutationFn: (payload: CreateDiagnosticServicePayload) => createDiagnosticServiceApi(payload),
    onSuccess: invalidate,
  });
}

export function useUpdateDiagnosticService() {
  const invalidate = useInvalidateDiagnosticServices();
  return useMutation({
    mutationFn: ({ id, payload }: { id: number; payload: UpdateDiagnosticServicePayload }) =>
      updateDiagnosticServiceApi(id, payload),
    onSuccess: invalidate,
  });
}

export function useDeactivateDiagnosticService() {
  const invalidate = useInvalidateDiagnosticServices();
  return useMutation({
    mutationFn: ({ id, payload }: { id: number; payload: DeactivateDiagnosticServicePayload }) =>
      deactivateDiagnosticServiceApi(id, payload),
    onSuccess: invalidate,
  });
}
