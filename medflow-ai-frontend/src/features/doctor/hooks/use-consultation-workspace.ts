import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  completeVisit,
  createDiagnosticOrder,
  fetchConsultationWorkspace,
  fetchDiagnosticOrderCatalog,
  fetchDiagnosticResultsReview,
  openConsultationWorkspace,
  recordClinicalExamination,
  recordDiagnosis,
  reviewDiagnosticResults,
  savePrescription,
  searchIcd10Codes,
} from '../api/consultation-workspace.api';
import type {
  CompleteVisitRequest,
  ConsultationWorkspaceResponseDto,
  CreateDiagnosticOrderRequest,
  CreatePrescriptionRequest,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
  RecordDiagnosisRequest,
} from '../types/consultation-workspace.types';
import type { ReviewDiagnosticResultsRequest } from '../types/review-diagnostic.types';

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

export interface RecordDiagnosisVariables {
  idOrCode: string;
  payload: RecordDiagnosisRequest;
}

export function useRecordDiagnosis() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, RecordDiagnosisVariables>({
    mutationFn: ({ idOrCode, payload }) => recordDiagnosis(idOrCode, payload),
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
  });
}

export interface SavePrescriptionVariables {
  idOrCode: string;
  payload: CreatePrescriptionRequest;
}

export function useSavePrescription() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, SavePrescriptionVariables>({
    mutationFn: ({ idOrCode, payload }) => savePrescription(idOrCode, payload),
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
  });
}

export function useIcd10CatalogSearch(params: { q: string; primaryOnly: boolean }, enabled = true) {
  return useQuery({
    queryKey: ['doctor', 'icd10-codes', params.q.trim(), params.primaryOnly],
    queryFn: () => searchIcd10Codes({ q: params.q, primaryOnly: params.primaryOnly, limit: 20 }),
    enabled,
    staleTime: 5 * 60_000,
    placeholderData: keepPreviousData,
  });
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

export function useDiagnosticResultsReview(idOrCode: string, enabled = true) {
  const normalized = idOrCode.trim();
  return useQuery({
    queryKey: ['doctor', 'consultation', normalized, 'diagnostic-results'],
    queryFn: () => fetchDiagnosticResultsReview(normalized),
    enabled: enabled && normalized.length > 0,
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
}

export interface ReviewDiagnosticResultsVariables {
  idOrCode: string;
  payload: ReviewDiagnosticResultsRequest;
}

export function useReviewDiagnosticResults() {
  const queryClient = useQueryClient();

  return useMutation<ConsultationWorkspaceResponseDto, unknown, ReviewDiagnosticResultsVariables>({
    mutationFn: ({ idOrCode, payload }) => reviewDiagnosticResults(idOrCode, payload),
    onSuccess: (data, variables) => {
      const normalized = variables.idOrCode.trim();
      queryClient.setQueryData(['doctor', 'consultation', normalized], data);
      if (data.visit?.visitCode) {
        queryClient.setQueryData(['doctor', 'consultation', data.visit.visitCode], data);
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
        queryKey: ['doctor', 'diagnostic-results', 'tracking'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'dashboard'],
      });
    },
  });
}
