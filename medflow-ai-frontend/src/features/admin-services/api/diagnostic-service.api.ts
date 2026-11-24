import { apiClient } from '@/shared/lib/axios';

import type {
  CreateDiagnosticServicePayload,
  DeactivateDiagnosticServicePayload,
  DiagnosticServiceFormOptions,
  DiagnosticServiceItem,
  DiagnosticServiceListParams,
  DiagnosticServiceListResponse,
  UpdateDiagnosticServicePayload,
} from '../types/diagnostic-service.types';

export async function fetchDiagnosticServicesApi(
  params: DiagnosticServiceListParams,
): Promise<DiagnosticServiceListResponse> {
  const { data } = await apiClient.get<DiagnosticServiceListResponse>(
    '/admin/diagnostic-services',
    { params },
  );
  return data;
}

export async function fetchDiagnosticServiceFormOptionsApi(): Promise<DiagnosticServiceFormOptions> {
  const { data } = await apiClient.get<DiagnosticServiceFormOptions>(
    '/admin/diagnostic-services/form-options',
  );
  return data;
}

export async function createDiagnosticServiceApi(
  payload: CreateDiagnosticServicePayload,
): Promise<DiagnosticServiceItem> {
  const { data } = await apiClient.post<DiagnosticServiceItem>(
    '/admin/diagnostic-services',
    payload,
  );
  return data;
}

export async function updateDiagnosticServiceApi(
  id: number,
  payload: UpdateDiagnosticServicePayload,
): Promise<DiagnosticServiceItem> {
  const { data } = await apiClient.put<DiagnosticServiceItem>(
    `/admin/diagnostic-services/${id}`,
    payload,
  );
  return data;
}

export async function deactivateDiagnosticServiceApi(
  id: number,
  payload: DeactivateDiagnosticServicePayload,
): Promise<DiagnosticServiceItem> {
  const { data } = await apiClient.patch<DiagnosticServiceItem>(
    `/admin/diagnostic-services/${id}/deactivate`,
    payload,
  );
  return data;
}
