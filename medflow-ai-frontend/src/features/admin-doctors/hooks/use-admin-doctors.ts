import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createAdminDoctorApi,
  deactivateAdminDoctorApi,
  fetchAdminDoctorsApi,
  reactivateAdminDoctorApi,
  updateAdminDoctorApi,
} from '../api/doctor-admin.api';
import type { Doctor } from '../types/doctor.types';

export const adminDoctorsQueryKey = ['admin', 'doctors'] as const;

export function useAdminDoctors() {
  return useQuery({
    queryKey: adminDoctorsQueryKey,
    queryFn: fetchAdminDoctorsApi,
  });
}

export function useCreateAdminDoctorMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createAdminDoctorApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminDoctorsQueryKey });
    },
  });
}

export function useUpdateAdminDoctorMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ idOrCode, doctor }: { idOrCode: string; doctor: Doctor }) =>
      updateAdminDoctorApi(idOrCode, doctor),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminDoctorsQueryKey });
    },
  });
}

export function useDeactivateAdminDoctorMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ idOrCode, reason }: { idOrCode: string; reason?: string }) =>
      deactivateAdminDoctorApi(idOrCode, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminDoctorsQueryKey });
    },
  });
}

export function useReactivateAdminDoctorMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: reactivateAdminDoctorApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminDoctorsQueryKey });
    },
  });
}
