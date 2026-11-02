import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';
import type {
  ExaminationService,
  ServiceSearchParams,
  ServiceSearchResponse,
} from '../types/service-search.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if (import.meta.env.MODE !== 'test') return true;
  return 'mock' in apiClient.get;
}

export function searchMockExaminationServices(
  params: ServiceSearchParams = {},
): ServiceSearchResponse {
  let filtered: ExaminationService[] = [...MOCK_EXAMINATION_SERVICES];

  // 1. Lọc theo từ khóa tìm kiếm (query)
  if (params.query?.trim()) {
    const q = params.query.trim().toLowerCase();
    filtered = filtered.filter(
      (s) =>
        s.name.toLowerCase().includes(q) ||
        s.serviceCode.toLowerCase().includes(q) ||
        s.departmentName.toLowerCase().includes(q) ||
        s.description.toLowerCase().includes(q) ||
        s.tags.some((tag) => tag.toLowerCase().includes(q)),
    );
  }

  // 2. Lọc theo phân loại (category)
  if (params.category && params.category !== 'all') {
    filtered = filtered.filter((s) => s.category === params.category);
  }

  // 3. Lọc theo khoa phòng (department)
  if (params.department && params.department !== 'all') {
    filtered = filtered.filter(
      (s) => s.departmentId === params.department || s.departmentName === params.department,
    );
  }

  // 4. Lọc theo mức giá / thời lượng khám (priceDurationRange)
  if (params.priceDurationRange && params.priceDurationRange !== 'all') {
    switch (params.priceDurationRange) {
      case 'under-200k':
        filtered = filtered.filter((s) => s.price <= 200000);
        break;
      case '200k-250k':
        filtered = filtered.filter((s) => s.price > 200000 && s.price <= 250000);
        break;
      case 'over-250k':
        filtered = filtered.filter((s) => s.price > 250000);
        break;
      case 'duration-under-20':
        filtered = filtered.filter((s) => s.durationMinutes <= 20);
        break;
      case 'duration-over-20':
        filtered = filtered.filter((s) => s.durationMinutes > 20);
        break;
      default:
        break;
    }
  }

  const total = filtered.length;
  const page = Math.max(1, params.page || 1);
  const pageSize = Math.max(1, params.pageSize || 6);
  const totalPages = Math.ceil(total / pageSize) || 1;

  const startIndex = (page - 1) * pageSize;
  const paginatedData = filtered.slice(startIndex, startIndex + pageSize);

  return {
    data: paginatedData,
    total,
    page,
    pageSize,
    totalPages,
  };
}

export async function searchExaminationServicesApi(
  params: ServiceSearchParams = {},
): Promise<ServiceSearchResponse> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<ServiceSearchResponse>('/examination-services', {
        params: {
          query: params.query?.trim() || undefined,
          category: params.category ?? 'all',
          department: params.department ?? 'all',
          priceDurationRange: params.priceDurationRange ?? 'all',
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 6,
        },
      });
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return searchMockExaminationServices(params);
}

export async function getExaminationServiceDetailApi(
  serviceId: string,
): Promise<ExaminationService | null> {
  const normalized = serviceId.trim();
  if (!normalized) return null;

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<ExaminationService>(
        `/examination-services/${encodeURIComponent(normalized)}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const service = MOCK_EXAMINATION_SERVICES.find(
    (s) => s.id === normalized || s.serviceCode.toLowerCase() === normalized.toLowerCase(),
  );
  return service || null;
}
