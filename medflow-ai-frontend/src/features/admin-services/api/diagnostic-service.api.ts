import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_SERVICES_MOCK } from '../data/diagnostic-services-data';
import type {
  CreateDiagnosticServicePayload,
  DeactivateDiagnosticServicePayload,
  DiagnosticServiceFormOptions,
  DiagnosticServiceItem,
  DiagnosticServiceListParams,
  DiagnosticServiceListResponse,
  UpdateDiagnosticServicePayload,
} from '../types/diagnostic-service.types';

const WRITE_REQUIRES_LOGIN =
  'Cần đăng nhập tài khoản Quản trị bệnh viện để thay đổi danh mục dịch vụ cận lâm sàng.';

function shouldUseHttpApi(method: 'get' | 'post' | 'put' | 'patch' = 'get'): boolean {
  if ('mock' in apiClient[method]) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

function buildFormOptionsMock(): DiagnosticServiceFormOptions {
  const departments = new Map<number, DiagnosticServiceFormOptions['departments'][number]>();
  const specialties = new Map<number, DiagnosticServiceFormOptions['specialties'][number]>();

  for (const item of DIAGNOSTIC_SERVICES_MOCK.data) {
    if (item.departmentId !== null && item.departmentName) {
      if (!departments.has(item.departmentId)) {
        departments.set(item.departmentId, {
          id: item.departmentId,
          departmentCode: item.departmentCode,
          name: item.departmentName,
        });
      }
    }
    if (item.specialtyId !== null && item.specialtyName) {
      if (!specialties.has(item.specialtyId)) {
        specialties.set(item.specialtyId, {
          id: item.specialtyId,
          specialtyCode: item.specialtyCode,
          name: item.specialtyName,
        });
      }
    }
  }

  const byName = (a: { name: string }, b: { name: string }) => a.name.localeCompare(b.name, 'vi');
  return {
    hospitalId: DIAGNOSTIC_SERVICES_MOCK.hospitalId,
    departments: [...departments.values()].sort(byName),
    specialties: [...specialties.values()].sort(byName),
  };
}

function queryDiagnosticServicesMock(
  params: DiagnosticServiceListParams,
): DiagnosticServiceListResponse {
  const normalizedKeyword = params.keyword?.trim().toLowerCase() ?? '';

  const filtered = DIAGNOSTIC_SERVICES_MOCK.data.filter((item) => {
    if (normalizedKeyword) {
      const matchesCode = item.serviceCode.toLowerCase().includes(normalizedKeyword);
      const matchesName = item.name.toLowerCase().includes(normalizedKeyword);
      const matchesDesc = Boolean(
        item.description && item.description.toLowerCase().includes(normalizedKeyword),
      );
      if (!matchesCode && !matchesName && !matchesDesc) {
        return false;
      }
    }

    if (params.departmentId !== undefined && item.departmentId !== params.departmentId) {
      return false;
    }

    if (params.status !== undefined && item.status !== params.status) {
      return false;
    }

    if (
      params.requiresResultVerification !== undefined &&
      item.requiresResultVerification !== params.requiresResultVerification
    ) {
      return false;
    }

    return true;
  });

  const sorted = [...filtered].sort((a, b) => a.id - b.id);
  const total = sorted.length;
  const page = Math.max(1, params.page);
  const pageSize = Math.max(1, params.pageSize);
  const totalPages = total > 0 ? Math.ceil(total / pageSize) : 0;
  const start = (page - 1) * pageSize;
  const data = sorted.slice(start, start + pageSize);

  return {
    ...DIAGNOSTIC_SERVICES_MOCK,
    data,
    total,
    page,
    pageSize,
    totalPages,
  };
}

export async function fetchDiagnosticServicesApi(
  params: DiagnosticServiceListParams,
): Promise<DiagnosticServiceListResponse> {
  if (shouldUseHttpApi('get')) {
    try {
      const { data } = await apiClient.get<DiagnosticServiceListResponse>(
        '/admin/diagnostic-services',
        { params },
      );
      return data;
    } catch (error) {
      // Only fallback to mock on network/offline errors; rethrow all HTTP 4xx/5xx responses.
      if (!axios.isAxiosError(error) || error.response) {
        throw error;
      }
    }
  }

  return queryDiagnosticServicesMock(params);
}

export async function fetchDiagnosticServiceFormOptionsApi(): Promise<DiagnosticServiceFormOptions> {
  if (shouldUseHttpApi('get')) {
    try {
      const { data } = await apiClient.get<DiagnosticServiceFormOptions>(
        '/admin/diagnostic-services/form-options',
      );
      return data;
    } catch (error) {
      if (!axios.isAxiosError(error) || error.response) {
        throw error;
      }
    }
  }

  return buildFormOptionsMock();
}

export async function createDiagnosticServiceApi(
  payload: CreateDiagnosticServicePayload,
): Promise<DiagnosticServiceItem> {
  if (!shouldUseHttpApi('post')) {
    throw new Error(WRITE_REQUIRES_LOGIN);
  }
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
  if (!shouldUseHttpApi('put')) {
    throw new Error(WRITE_REQUIRES_LOGIN);
  }
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
  if (!shouldUseHttpApi('patch')) {
    throw new Error(WRITE_REQUIRES_LOGIN);
  }
  const { data } = await apiClient.patch<DiagnosticServiceItem>(
    `/admin/diagnostic-services/${id}/deactivate`,
    payload,
  );
  return data;
}
