import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  cancelClinicalRecoveryApi,
  confirmClinicalRecoveryApi,
  createClinicalRecoveryRequestApi,
  fetchClinicalRecoveryContextApi,
  grantClinicalRecoveryApi,
  rejectClinicalRecoveryApi,
  revokeClinicalRecoveryApi,
} from '../api/clinical-recovery.api';
import type {
  ClinicalRecoveryDecisionPayload,
  ConfirmClinicalRecoveryRequestPayload,
  CreateClinicalRecoveryRequestPayload,
  GrantClinicalRecoveryRequestPayload,
} from '../types/clinical-recovery.types';

const recoveryQueryKey = (visitIdOrCode: string) =>
  ['clinical-recovery', visitIdOrCode.trim()] as const;

export function useClinicalRecoveryContext(visitIdOrCode: string) {
  const trimmed = visitIdOrCode.trim();
  return useQuery({
    queryKey: recoveryQueryKey(trimmed),
    queryFn: () => fetchClinicalRecoveryContextApi(trimmed),
    enabled: trimmed.length > 0,
    retry: false,
  });
}

/**
 * Mutations for one visit's recovery context. Every mutation invalidates the context so capability
 * flags and request states are re-read from the server instead of being patched locally.
 */
export function useClinicalRecoveryActions(visitIdOrCode: string) {
  const queryClient = useQueryClient();
  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: recoveryQueryKey(visitIdOrCode) });

  const request = useMutation({
    mutationFn: (payload: CreateClinicalRecoveryRequestPayload) =>
      createClinicalRecoveryRequestApi(visitIdOrCode, payload),
    onSuccess: invalidate,
  });

  const grant = useMutation({
    mutationFn: ({
      requestId,
      payload,
    }: {
      requestId: number;
      payload: GrantClinicalRecoveryRequestPayload;
    }) => grantClinicalRecoveryApi(requestId, payload),
    onSuccess: invalidate,
  });

  const reject = useMutation({
    mutationFn: ({
      requestId,
      payload,
    }: {
      requestId: number;
      payload: ClinicalRecoveryDecisionPayload;
    }) => rejectClinicalRecoveryApi(requestId, payload),
    onSuccess: invalidate,
  });

  const revoke = useMutation({
    mutationFn: ({
      requestId,
      payload,
    }: {
      requestId: number;
      payload: ClinicalRecoveryDecisionPayload;
    }) => revokeClinicalRecoveryApi(requestId, payload),
    onSuccess: invalidate,
  });

  const cancel = useMutation({
    mutationFn: ({
      requestId,
      payload,
    }: {
      requestId: number;
      payload: ClinicalRecoveryDecisionPayload;
    }) => cancelClinicalRecoveryApi(requestId, payload),
    onSuccess: invalidate,
  });

  const confirm = useMutation({
    mutationFn: ({
      requestId,
      payload,
    }: {
      requestId: number;
      payload: ConfirmClinicalRecoveryRequestPayload;
    }) => confirmClinicalRecoveryApi(requestId, payload),
    onSuccess: invalidate,
  });

  return { request, grant, reject, revoke, cancel, confirm };
}
