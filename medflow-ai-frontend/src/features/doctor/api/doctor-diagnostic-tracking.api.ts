import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { resolveMockDoctorDiagnosticTracking } from '../data/doctor-diagnostic-tracking-mock';
import type {
  DoctorDiagnosticTrackingQueryParams,
  DoctorDiagnosticTrackingResponseDto,
} from '../types/doctor-diagnostic-tracking.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response;
}

function buildQueryParams(
  params?: DoctorDiagnosticTrackingQueryParams,
): Record<string, string | number> | undefined {
  if (!params) return undefined;
  const cleaned: Record<string, string | number> = {};
  if (params.date && params.date.trim() !== '') {
    cleaned.date = params.date.trim();
  }
  if (params.status) {
    cleaned.status = params.status;
  }
  if (params.keyword && params.keyword.trim() !== '') {
    cleaned.keyword = params.keyword.trim();
  }
  if (typeof params.page === 'number') {
    cleaned.page = params.page;
  }
  if (typeof params.pageSize === 'number') {
    cleaned.pageSize = params.pageSize;
  }
  return Object.keys(cleaned).length > 0 ? cleaned : undefined;
}

export async function fetchDoctorDiagnosticTracking(
  params?: DoctorDiagnosticTrackingQueryParams,
): Promise<DoctorDiagnosticTrackingResponseDto> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const cleanedParams = buildQueryParams(params);
      const { data } = await apiClient.get<DoctorDiagnosticTrackingResponseDto>(
        '/doctor/diagnostic-results/tracking',
        cleanedParams ? { params: cleanedParams } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDoctorDiagnosticTracking(params);
}
