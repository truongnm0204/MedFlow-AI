import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import {
  applyEarlyArrivalBackfill,
  callNextEligiblePatient,
  createReturnToDoctorQueueEntry,
  fetchExaminationQueue,
  markQueueEntryMissedTurn,
  restoreSkippedQueueTurn,
  updateExaminationQueueStatus,
} from '../api/examination-queue.api';
import { applyQueueStatusUpdateToResponse } from '../data/examination-queue-mock';
import type {
  CallNextPatientRequestPayload,
  CallNextPatientResponseDto,
  CreateReturnToDoctorQueueEntryRequestPayload,
  CreateReturnToDoctorQueueEntryResponseDto,
  EarlyArrivalBackfillRequestPayload,
  ExaminationQueueQueryParams,
  ExaminationQueueResponseDto,
  MarkMissedTurnRequestPayload,
  QueueTurnActionResponseDto,
  RestoreQueueTurnRequestPayload,
  UpdateExaminationQueueStatusPayload,
  UpdateExaminationQueueStatusResponseDto,
} from '../types/examination-queue.types';
const HOSPITAL_QUEUE_CHANNEL: RealtimeChannel = { type: 'hospital-queue' };
const STAFF_QUEUE_CONNECTED_POLL_MS = 30_000;
const STAFF_QUEUE_FALLBACK_POLL_MS = 15_000;

export function useExaminationQueue(params?: ExaminationQueueQueryParams, enabled = true) {
  const queryClient = useQueryClient();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const handleRealtimeQueueChanged = useCallback(() => {
    scheduleCoalescedInvalidation(queryClient, ['reception', 'examination-queue']);
  }, [queryClient]);

  const { isLive } = useRealtimeSubscription({
    channel: HOSPITAL_QUEUE_CHANNEL,
    event: 'QueueChanged',
    onEvent: handleRealtimeQueueChanged,
    onReconnected: handleRealtimeQueueChanged,
    enabled: enabled && isAuthenticated,
  });

  return useQuery({
    queryKey: ['reception', 'examination-queue', params ?? null],
    queryFn: () => fetchExaminationQueue(params),
    enabled,
    staleTime: 30_000,
    refetchInterval:
      enabled && isAuthenticated
        ? isLive
          ? STAFF_QUEUE_CONNECTED_POLL_MS
          : STAFF_QUEUE_FALLBACK_POLL_MS
        : false,
  });
}

export interface ApplyEarlyArrivalBackfillVariables {
  queueEntryId: string;
  payload: EarlyArrivalBackfillRequestPayload;
}

export function useApplyEarlyArrivalBackfill() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ queueEntryId, payload }: ApplyEarlyArrivalBackfillVariables) =>
      applyEarlyArrivalBackfill(queueEntryId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
    },
  });
}

export interface UpdateExaminationQueueStatusVariables {
  queueEntryId: string;
  payload: UpdateExaminationQueueStatusPayload;
  currentQueueData?: ExaminationQueueResponseDto;
}

export function useUpdateExaminationQueueStatus() {
  const queryClient = useQueryClient();

  return useMutation<
    UpdateExaminationQueueStatusResponseDto,
    unknown,
    UpdateExaminationQueueStatusVariables
  >({
    mutationFn: ({ queueEntryId, payload, currentQueueData }) =>
      updateExaminationQueueStatus(queueEntryId, payload, currentQueueData),
    onSuccess: (result) => {
      queryClient.setQueriesData<ExaminationQueueResponseDto>(
        { queryKey: ['reception', 'examination-queue'] },
        (old) => (old ? applyQueueStatusUpdateToResponse(old, result.entry) : old),
      );
    },
  });
}

export interface MarkMissedTurnVariables {
  idOrCode: string;
  payload: MarkMissedTurnRequestPayload;
}

export function useMarkMissedTurn() {
  const queryClient = useQueryClient();

  return useMutation<QueueTurnActionResponseDto, unknown, MarkMissedTurnVariables>({
    mutationFn: ({ idOrCode, payload }) => markQueueEntryMissedTurn(idOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
    },
  });
}

export interface RestoreSkippedTurnVariables {
  idOrCode: string;
  payload: RestoreQueueTurnRequestPayload;
}

export function useRestoreSkippedTurn() {
  const queryClient = useQueryClient();

  return useMutation<QueueTurnActionResponseDto, unknown, RestoreSkippedTurnVariables>({
    mutationFn: ({ idOrCode, payload }) => restoreSkippedQueueTurn(idOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
    },
  });
}

export interface CallNextEligiblePatientVariables {
  servicePointId: string;
  payload?: CallNextPatientRequestPayload;
}

export function useCallNextEligiblePatient() {
  const queryClient = useQueryClient();

  return useMutation<CallNextPatientResponseDto, unknown, CallNextEligiblePatientVariables>({
    mutationFn: ({ servicePointId, payload }) => callNextEligiblePatient(servicePointId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
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

export interface CreateReturnToDoctorQueueEntryVariables {
  idOrCode: string;
  payload: CreateReturnToDoctorQueueEntryRequestPayload;
}

export function useCreateReturnToDoctorQueueEntry() {
  const queryClient = useQueryClient();

  return useMutation<
    CreateReturnToDoctorQueueEntryResponseDto,
    unknown,
    CreateReturnToDoctorQueueEntryVariables
  >({
    mutationFn: ({ idOrCode, payload }) => createReturnToDoctorQueueEntry(idOrCode, payload),
    onSuccess: (data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['reception', 'examination-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['queue-entry-status', variables.idOrCode.trim()],
      });
      if (data.queueEntryId && data.queueEntryId.trim() !== variables.idOrCode.trim()) {
        void queryClient.invalidateQueries({
          queryKey: ['queue-entry-status', data.queueEntryId.trim()],
        });
      }
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'assigned-queue'],
      });
      void queryClient.invalidateQueries({
        queryKey: ['doctor', 'dashboard'],
      });
    },
  });
}
