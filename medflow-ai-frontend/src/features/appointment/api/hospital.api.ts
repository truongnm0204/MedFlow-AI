import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_HOSPITALS } from '../data/hospital-mock';
import type { Hospital, HospitalDto } from '../types/booking-entry.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if (import.meta.env.MODE !== 'test') return true;
  return 'mock' in apiClient.get;
}

function mapHospitalDto(dto: HospitalDto): Hospital {
  return {
    id: String(dto.id),
    hospitalCode: dto.hospitalCode ?? '',
    name: dto.name ?? 'Cơ sở chưa đặt tên',
    address: dto.address ?? '',
    isActive: dto.isActive === true,
  };
}

/**
 * Lấy danh sách cơ sở khám. Backend `GET /api/v1/hospitals` chưa ship
 * (ADR-0006) → 404/network/test-mode trả về `MOCK_HOSPITALS`; lỗi HTTP khác rethrow.
 */
export async function fetchHospitalsApi(): Promise<Hospital[]> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<HospitalDto[]>('/hospitals');
      return data.map(mapHospitalDto);
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return MOCK_HOSPITALS;
}
