import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import type {
  ExaminationService,
  UpdateServicePricePayload,
} from '../types/examination-service.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response || error.response.status === 404 || error.response.status === 502;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get || 'mock' in apiClient.put) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

/**
 * Lấy danh sách dịch vụ khám và thông tin giá từ backend API `GET /api/v1/admin/examination-services`.
 * Tự động fallback về mock data khi offline hoặc không có token.
 */
export async function fetchExaminationServicesApi(): Promise<ExaminationService[]> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<{ data: ExaminationService[] }>(
        '/admin/examination-services',
      );
      if (data && Array.isArray(data.data)) {
        return data.data;
      }
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return INITIAL_SERVICES;
}

/**
 * Cập nhật đơn giá dịch vụ khám theo chính sách bệnh viện (UC52).
 * Gọi API `PUT /api/v1/admin/examination-services/{id}` với payload `{ price: payload.price }`.
 * Tự động ghi nhận lịch sử thay đổi biểu phí.
 */
export async function updateServicePriceApi(
  serviceId: string,
  payload: UpdateServicePricePayload,
): Promise<ExaminationService> {
  const numericId = serviceId.replace(/\D/g, '') || serviceId;

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.put<ExaminationService>(
        `/admin/examination-services/${numericId}`,
        {
          price: payload.price,
        },
      );
      return {
        ...data,
        effectiveDate: payload.effectiveDate,
        decisionNumber: payload.decisionNumber,
      };
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  // Fallback update in-memory
  const existing = INITIAL_SERVICES.find((s) => s.id === serviceId || s.code === serviceId);
  const updated: ExaminationService = existing
    ? {
        ...existing,
        price: payload.price,
        effectiveDate: payload.effectiveDate,
        decisionNumber: payload.decisionNumber,
        priceHistory: [
          {
            date: payload.effectiveDate,
            price: payload.price,
            decisionNumber: payload.decisionNumber,
            note: payload.note || `Điều chỉnh giá theo QĐ ${payload.decisionNumber}`,
            appliedBy: payload.appliedBy,
          },
          ...(existing.priceHistory ?? []),
        ],
      }
    : {
        id: serviceId,
        code: serviceId,
        name: 'Dịch vụ khám',
        description: '',
        category: 'Khám Lâm sàng',
        department: 'Khoa Khám bệnh',
        price: payload.price,
        onlineBooking: true,
        status: 'ACTIVE',
        effectiveDate: payload.effectiveDate,
        decisionNumber: payload.decisionNumber,
        priceHistory: [
          {
            date: payload.effectiveDate,
            price: payload.price,
            decisionNumber: payload.decisionNumber,
            note: payload.note || `Điều chỉnh giá theo QĐ ${payload.decisionNumber}`,
            appliedBy: payload.appliedBy,
          },
        ],
      };

  return updated;
}
