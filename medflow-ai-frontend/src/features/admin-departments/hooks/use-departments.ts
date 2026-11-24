import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createDepartmentApi,
  deactivateDepartmentApi,
  fetchDepartmentsApi,
  fetchHospitalsForAdminApi,
  reactivateDepartmentApi,
  updateDepartmentApi,
} from '../api/department.api';
import type { DeactivateDepartmentPayload } from '../types/department-deactivate.types';
import type { DepartmentItem } from '../types/department.types';

export const ADMIN_DEPARTMENTS_QUERY_KEY = ['admin', 'departments'] as const;

export function useAdminDepartments() {
  return useQuery({
    queryKey: ADMIN_DEPARTMENTS_QUERY_KEY,
    queryFn: fetchDepartmentsApi,
    retry: false,
  });
}

export function useAdminHospitalOptions() {
  return useQuery({
    queryKey: ['admin', 'hospitals'] as const,
    queryFn: fetchHospitalsForAdminApi,
    retry: false,
  });
}

export function useCreateDepartmentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createDepartmentApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_DEPARTMENTS_QUERY_KEY });
    },
  });
}

export function useUpdateDepartmentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      payload,
    }: {
      id: string | number;
      payload: { code: string; name: string; hospitalId?: number | null; isActive: boolean };
    }) => updateDepartmentApi(id, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_DEPARTMENTS_QUERY_KEY });
    },
  });
}

export function useDeactivateDepartmentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      department,
      payload,
    }: {
      department: DepartmentItem;
      payload?: DeactivateDepartmentPayload;
    }) => deactivateDepartmentApi(department, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_DEPARTMENTS_QUERY_KEY });
    },
  });
}

export function useReactivateDepartmentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (department: DepartmentItem) => reactivateDepartmentApi(department),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_DEPARTMENTS_QUERY_KEY });
    },
  });
}
