import { apiClient } from '@/shared/lib/axios';

import type {
  ClinicalRecoveryConfirmResponseDto,
  ClinicalRecoveryContextDto,
  ClinicalRecoveryDecisionPayload,
  ClinicalRecoveryRequestDto,
  ConfirmClinicalRecoveryRequestPayload,
  CreateClinicalRecoveryRequestPayload,
  GrantClinicalRecoveryRequestPayload,
} from '../types/clinical-recovery.types';

const visitRecoveryPath = (visitIdOrCode: string) =>
  `/visits/${encodeURIComponent(visitIdOrCode.trim())}`;

const requestRecoveryPath = (requestId: number) =>
  `/clinical-session-recovery-requests/${requestId}`;

/** GET /api/v1/visits/{idOrCode}/clinical-session-recovery-context */
export async function fetchClinicalRecoveryContextApi(
  visitIdOrCode: string,
): Promise<ClinicalRecoveryContextDto> {
  const { data } = await apiClient.get<ClinicalRecoveryContextDto>(
    `${visitRecoveryPath(visitIdOrCode)}/clinical-session-recovery-context`,
  );
  return data;
}

/** POST /api/v1/visits/{idOrCode}/clinical-session-recovery-requests */
export async function createClinicalRecoveryRequestApi(
  visitIdOrCode: string,
  payload: CreateClinicalRecoveryRequestPayload,
): Promise<ClinicalRecoveryRequestDto> {
  const { data } = await apiClient.post<ClinicalRecoveryRequestDto>(
    `${visitRecoveryPath(visitIdOrCode)}/clinical-session-recovery-requests`,
    payload,
  );
  return data;
}

/** POST /api/v1/clinical-session-recovery-requests/{id}/grant */
export async function grantClinicalRecoveryApi(
  requestId: number,
  payload: GrantClinicalRecoveryRequestPayload,
): Promise<ClinicalRecoveryRequestDto> {
  const { data } = await apiClient.post<ClinicalRecoveryRequestDto>(
    `${requestRecoveryPath(requestId)}/grant`,
    payload,
  );
  return data;
}

/** POST /api/v1/clinical-session-recovery-requests/{id}/reject */
export async function rejectClinicalRecoveryApi(
  requestId: number,
  payload: ClinicalRecoveryDecisionPayload,
): Promise<ClinicalRecoveryRequestDto> {
  const { data } = await apiClient.post<ClinicalRecoveryRequestDto>(
    `${requestRecoveryPath(requestId)}/reject`,
    payload,
  );
  return data;
}

/** POST /api/v1/clinical-session-recovery-requests/{id}/revoke */
export async function revokeClinicalRecoveryApi(
  requestId: number,
  payload: ClinicalRecoveryDecisionPayload,
): Promise<ClinicalRecoveryRequestDto> {
  const { data } = await apiClient.post<ClinicalRecoveryRequestDto>(
    `${requestRecoveryPath(requestId)}/revoke`,
    payload,
  );
  return data;
}

/** POST /api/v1/clinical-session-recovery-requests/{id}/cancel */
export async function cancelClinicalRecoveryApi(
  requestId: number,
  payload: ClinicalRecoveryDecisionPayload,
): Promise<ClinicalRecoveryRequestDto> {
  const { data } = await apiClient.post<ClinicalRecoveryRequestDto>(
    `${requestRecoveryPath(requestId)}/cancel`,
    payload,
  );
  return data;
}

/** POST /api/v1/clinical-session-recovery-requests/{id}/confirm */
export async function confirmClinicalRecoveryApi(
  requestId: number,
  payload: ConfirmClinicalRecoveryRequestPayload,
): Promise<ClinicalRecoveryConfirmResponseDto> {
  const { data } = await apiClient.post<ClinicalRecoveryConfirmResponseDto>(
    `${requestRecoveryPath(requestId)}/confirm`,
    payload,
  );
  return data;
}
