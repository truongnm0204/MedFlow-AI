import { apiClient } from '@/shared/lib/axios';

import type {
  CreateEkycSessionRequest,
  CreateEkycSessionResponse,
  CreateReceptionEkycSessionRequest,
  CreateUnverifiedPatientProfileRequest,
  CreateUnverifiedPatientProfileResponse,
  EkycSessionStatusResponse,
  FinalizeEkycSessionResponse,
} from '../types/ekyc.types';

export async function createEkycSession(
  payload: CreateEkycSessionRequest,
): Promise<CreateEkycSessionResponse> {
  const { data } = await apiClient.post<CreateEkycSessionResponse>('/ekyc/sessions', payload);
  return data;
}

export async function getEkycSession(sessionId: string): Promise<EkycSessionStatusResponse> {
  const { data } = await apiClient.get<EkycSessionStatusResponse>(`/ekyc/sessions/${sessionId}`);
  return data;
}

export async function finalizeEkycSession(sessionId: string): Promise<FinalizeEkycSessionResponse> {
  const { data } = await apiClient.post<FinalizeEkycSessionResponse>(
    `/ekyc/sessions/${sessionId}/finalize`,
  );
  return data;
}

// --- Reception-assisted endpoints (/api/v1/reception/ekyc) -------------------------
// Staff drive the SAME VNPT engine for a patient profile. Authorization is enforced server-side
// (role + PATIENT_IDENTITY_VERIFY permission). Verdict still comes only from finalize — the same
// /ekyc/sessions/{id}/finalize endpoint is used regardless of who drives the capture.

/** POST /api/v1/reception/ekyc/patients/{patientId}/sessions — start eKYC for a patient profile. */
export async function createReceptionEkycSession(
  patientId: number,
  payload: Omit<CreateReceptionEkycSessionRequest, 'patientId'>,
): Promise<CreateEkycSessionResponse> {
  const { data } = await apiClient.post<CreateEkycSessionResponse>(
    `/reception/ekyc/patients/${patientId}/sessions`,
    payload,
  );
  return data;
}

/** POST /api/v1/reception/ekyc/patients/unverified — create a profile WITHOUT eKYC (owner-approved exception). */
export async function createUnverifiedPatientProfile(
  payload: CreateUnverifiedPatientProfileRequest,
): Promise<CreateUnverifiedPatientProfileResponse> {
  const { data } = await apiClient.post<CreateUnverifiedPatientProfileResponse>(
    '/reception/ekyc/patients/unverified',
    payload,
  );
  return data;
}

/** GET /api/v1/reception/ekyc/sessions/{id} — status of a reception-driven session. */
export async function getReceptionEkycSession(
  sessionId: string,
): Promise<EkycSessionStatusResponse> {
  const { data } = await apiClient.get<EkycSessionStatusResponse>(
    `/reception/ekyc/sessions/${sessionId}`,
  );
  return data;
}
