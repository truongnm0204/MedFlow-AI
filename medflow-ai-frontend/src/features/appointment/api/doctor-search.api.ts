import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_DOCTORS } from '../data/doctor-search-mock';
import type { DoctorFilterParams, DoctorSearchResponse } from '../types/doctor-search.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if (import.meta.env.MODE !== 'test') return true;
  return 'mock' in apiClient.get;
}

export function searchMockDoctors(params: DoctorFilterParams = {}): DoctorSearchResponse {
  const {
    keyword = '',
    specialty = 'ALL',
    degree = 'ALL',
    hasScheduleToday = false,
    hasScheduleThisWeek = false,
    page = 1,
    pageSize = 6,
  } = params;

  let filtered = [...MOCK_DOCTORS];

  // 1. Keyword search (name, specialty, title, roleTag)
  if (keyword.trim()) {
    const q = keyword.trim().toLowerCase();
    filtered = filtered.filter(
      (doc) =>
        doc.name.toLowerCase().includes(q) ||
        doc.specialty.toLowerCase().includes(q) ||
        doc.roleTag.toLowerCase().includes(q) ||
        doc.title.toLowerCase().includes(q),
    );
  }

  // 2. Specialty category filter
  if (specialty && specialty !== 'ALL') {
    filtered = filtered.filter((doc) => doc.specialtyId === specialty);
  }

  // 3. Academic degree / title filter
  if (degree && degree !== 'ALL') {
    filtered = filtered.filter((doc) => doc.title === degree);
  }

  // 4. Schedule availability filters
  if (hasScheduleToday) {
    filtered = filtered.filter((doc) => doc.hasScheduleToday);
  }

  if (hasScheduleThisWeek) {
    filtered = filtered.filter((doc) => doc.hasScheduleThisWeek);
  }

  const total = filtered.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const startIndex = (safePage - 1) * pageSize;
  const items = filtered.slice(startIndex, startIndex + pageSize);

  return {
    items,
    total,
    page: safePage,
    pageSize,
    totalPages,
  };
}

export async function fetchDoctorsApi(
  params: DoctorFilterParams = {},
): Promise<DoctorSearchResponse> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<DoctorSearchResponse>('/doctors', {
        params: {
          keyword: params.keyword?.trim() || undefined,
          specialty: params.specialty ?? 'ALL',
          degree: params.degree ?? 'ALL',
          hasScheduleToday: params.hasScheduleToday ?? false,
          hasScheduleThisWeek: params.hasScheduleThisWeek ?? false,
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

  return searchMockDoctors(params);
}
