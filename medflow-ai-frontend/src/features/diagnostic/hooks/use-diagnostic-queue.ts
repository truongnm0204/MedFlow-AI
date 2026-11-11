import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  callDiagnosticQueueEntry,
  callNextDiagnosticPatient,
  cancelDiagnosticQueueEntry,
  completeDiagnosticQueueExecution,
  fetchDiagnosticQueueBoard,
  fetchDiagnosticQueueEntryDetail,
  markDiagnosticQueueMissedTurn,
  markDiagnosticQueueReady,
  restoreDiagnosticQueueTurn,
  startDiagnosticQueueExecution,
  updateDiagnosticQueueStatus,
} from '../api/diagnostic.api';
import type {
  CallNextDiagnosticPatientRequest,
  CancelDiagnosticQueueEntryRequest,
  DiagnosticQueueTurnActionRequest,
  GetDiagnosticQueueParams,
  UpdateDiagnosticQueueStatusRequest,
} from '../types/diagnostic.types';

export const DIAGNOSTIC_QUEUE_QUERY_KEY = ['diagnostic', 'queue'] as const;

export function useDiagnosticQueueBoard(params?: GetDiagnosticQueueParams, enabled = true) {
  return useQuery({
    queryKey: [...DIAGNOSTIC_QUEUE_QUERY_KEY, 'board', params ?? {}],
    queryFn: () => fetchDiagnosticQueueBoard(params),
    enabled,
    staleTime: 30_000,
  });
}

export function useDiagnosticQueueEntryDetail(idOrCode?: string | null, enabled = true) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: [...DIAGNOSTIC_QUEUE_QUERY_KEY, 'detail', normalized],
    queryFn: () => fetchDiagnosticQueueEntryDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 30_000,
  });
}

export function useCallNextDiagnosticPatient() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      servicePointId,
      payload,
    }: {
      servicePointId: string;
      payload?: CallNextDiagnosticPatientRequest;
    }) => callNextDiagnosticPatient(servicePointId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useUpdateDiagnosticQueueStatus() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: UpdateDiagnosticQueueStatusRequest;
    }) => updateDiagnosticQueueStatus(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useMarkDiagnosticQueueReady() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: DiagnosticQueueTurnActionRequest;
    }) => markDiagnosticQueueReady(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useCallDiagnosticQueueEntry() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: DiagnosticQueueTurnActionRequest;
    }) => callDiagnosticQueueEntry(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useStartDiagnosticQueueExecution() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: DiagnosticQueueTurnActionRequest;
    }) => startDiagnosticQueueExecution(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useCompleteDiagnosticQueueExecution() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: DiagnosticQueueTurnActionRequest;
    }) => completeDiagnosticQueueExecution(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useMarkDiagnosticQueueMissedTurn() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: DiagnosticQueueTurnActionRequest;
    }) => markDiagnosticQueueMissedTurn(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useRestoreDiagnosticQueueTurn() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: DiagnosticQueueTurnActionRequest;
    }) => restoreDiagnosticQueueTurn(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useCancelDiagnosticQueueEntry() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: CancelDiagnosticQueueEntryRequest;
    }) => cancelDiagnosticQueueEntry(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DIAGNOSTIC_QUEUE_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}
