import { apiClient } from '@/shared/lib/axios';

import type {
  CreateEkycSessionRequest,
  CreateEkycSessionResponse,
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

export async function finalizeEkycSession(
  sessionId: string,
): Promise<FinalizeEkycSessionResponse> {
  const { data } = await apiClient.post<FinalizeEkycSessionResponse>(
    `/ekyc/sessions/${sessionId}/finalize`,
  );
  return data;
}
