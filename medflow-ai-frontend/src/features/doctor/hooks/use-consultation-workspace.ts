import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  completeVisit,
  createDiagnosticOrder,
  fetchConsultationWorkspace,
  fetchDiagnosticOrderCatalog,
  openConsultationWorkspace,
  recordClinicalExamination,
} from '../api/consultation-workspace.api';
import type {
  CompleteVisitRequest,
  ConsultationWorkspaceResponseDto,
  CreateDiagnosticOrderRequest,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
} from '../types/consultation-workspace.types';

export function useConsultationWorkspace(idOrCode: string, enabled = true) {
  const normalized = idOrCode.trim();
  return useQuery({
    queryKey: ['doctor', 'consultation', normalized],
    queryFn: () => fetchConsultationWorkspace(normalized),
    enabled: enabled && normalized.length > 0,
    staleTime: 15_000,
  });
}

export interface OpenConsultationWorkspaceVariables {
  idOrCode: string;
  payload?: OpenConsultationWorkspaceRequest;
}

export function useOpenConsultationWorkspace() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, OpenConsultationWorkspaceVariables>(
    {
      mutationFn: ({ idOrCode, payload }) => openConsultationWorkspace(idOrCode, payload),
      onSuccess: (data, variables) => {
        const normalized = variables.idOrCode.trim();
        queryClient.setQueryData(['doctor', 'consultation', normalized], data);
        if (data.visit?.visitCode) {
          queryClient.setQueryData(['doctor', 'consultation', data.visit.visitCode], data);
        }
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'assigned-queue'],
        });
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'dashboard'],
        });
        void queryClient.invalidateQueries({
          queryKey: ['reception', 'examination-queue'],
        });
      },
    },
  );
}

export interface RecordClinicalExaminationVariables {
  idOrCode: string;
  payload: RecordClinicalExaminationRequest;
}

export function useRecordClinicalExamination() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, RecordClinicalExaminationVariables>(
    {
      mutationFn: ({ idOrCode, payload }) => recordClinicalExamination(idOrCode, payload),
      onSuccess: (data, variables) => {
        const normalized = variables.idOrCode.trim();
        queryClient.setQueryData(['doctor', 'consultation', normalized], data);
        if (data.visit?.visitCode) {
          queryClient.setQueryData(['doctor', 'consultation', data.visit.visitCode], data);
        }
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'medical-history'],
        });
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'assigned-queue'],
        });
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'dashboard'],
        });
      },
    },
  );
}

export function useDiagnosticOrderCatalog(idOrCode: string, enabled = true) {
  const normalized = idOrCode.trim();
  return useQuery({
    queryKey: ['doctor', 'consultation', normalized, 'diagnostic-orders'],
    queryFn: () => fetchDiagnosticOrderCatalog(normalized),
    enabled: enabled && normalized.length > 0,
    staleTime: 15_000,
  });
}

export interface CreateDiagnosticOrderVariables {
  idOrCode: string;
  payload: CreateDiagnosticOrderRequest;
}

export function useCreateDiagnosticOrder() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, CreateDiagnosticOrderVariables>({
    mutationFn: ({ idOrCode, payload }) => createDiagnosticOrder(idOrCode, payload),
    onSuccess: (data, variables) => {
      const normalized = variables.idOrCode.trim();
      queryClient.setQueryData(['doctor', 'consultation', normalized], data);
      if (data.visit?.visitCode) {
        queryClient.setQueryData(['doctor', 'consultation', data.visit.visitCode], data);
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'consultation', normalized, 'diagnostic-orders'],
      });
      if (data.visit?.visitCode) {
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'consultation', data.visit.visitCode, 'diagnostic-orders'],
        });
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'consultation', normalized, 'diagnostic-results'],
      });
      if (data.visit?.visitCode) {
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'consultation', data.visit.visitCode, 'diagnostic-results'],
        });
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'assigned-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'dashboard'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['diagnostic', 'dashboard'],
      });
    },
  });
}

export interface CompleteVisitVariables {
  idOrCode: string;
  payload?: CompleteVisitRequest;
}

export function useCompleteVisit() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, CompleteVisitVariables>({
    mutationFn: ({ idOrCode, payload }) => completeVisit(idOrCode, payload),
    onSuccess: (data, variables) => {
      const normalized = variables.idOrCode.trim();
      queryClient.setQueryData(['doctor', 'consultation', normalized], data);
      if (data.visit?.visitCode) {
        queryClient.setQueryData(['doctor', 'consultation', data.visit.visitCode], data);
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'consultation', normalized, 'diagnostic-orders'],
      });
      if (data.visit?.visitCode) {
        void queryClient.invalidateQueries({
          queryKey: ['doctor', 'consultation', data.visit.visitCode, 'diagnostic-orders'],
        });
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'assigned-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'dashboard'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'medical-history'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['pharmacy', 'dashboard'],
      });
    },
  });
}
