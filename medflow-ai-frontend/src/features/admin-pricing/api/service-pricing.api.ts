import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  calculateMetrics,
  INITIAL_PRICING_DATA,
  updateMockServicePrice,
} from '../data/service-pricing-mock';
import type {
  ServicePricingFilter,
  ServicePricingItem,
  ServicePricingResponse,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

function isNetworkOrGatewayError(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return [404, 502, 503, 504].includes(error.response.status);
}

export function filterPricingItems(
  items: ServicePricingItem[],
  filters: Partial<ServicePricingFilter>,
): ServicePricingItem[] {
  return items.filter((item) => {
    if (filters.search) {
      const q = filters.search.trim().toLowerCase();
      const match =
        item.code.toLowerCase().includes(q) ||
        item.name.toLowerCase().includes(q) ||
        item.description.toLowerCase().includes(q) ||
        item.decisionNumber.toLowerCase().includes(q);
      if (!match) return false;
    }

    if (
      filters.department &&
      filters.department !== 'Tất cả khoa phòng' &&
      filters.department !== 'all' &&
      item.department !== filters.department
    ) {
      return false;
    }

    if (filters.category && filters.category !== 'all' && item.category !== filters.category) {
      return false;
    }

    if (filters.priceRange && filters.priceRange !== 'all') {
      if (filters.priceRange === 'under-200k' && item.currentPrice >= 200000) return false;
      if (
        filters.priceRange === '200k-500k' &&
        (item.currentPrice < 200000 || item.currentPrice > 500000)
      ) {
        return false;
      }
      if (filters.priceRange === 'over-500k' && item.currentPrice <= 500000) return false;
    }

    if (filters.status && filters.status !== 'all' && item.status !== filters.status) {
      return false;
    }

    return true;
  });
}

interface BackendRawItem {
  id: number;
  serviceCode: string;
  name: string;
  description?: string;
  category?: ServicePricingItem['category'];
  departmentName?: string;
  price: number;
  priceFormatted?: string;
  isActive?: boolean;
  allowsOnlineBooking?: boolean;
}

export async function fetchServicePricing(
  filters: Partial<ServicePricingFilter> = {},
  page = 1,
  pageSize = 10,
): Promise<ServicePricingResponse> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get('/admin/examination-services', {
        params: {
          keyword: filters.search || undefined,
          category: filters.category !== 'all' ? filters.category : undefined,
          status: filters.status !== 'all' ? filters.status : undefined,
          page,
          pageSize,
        },
      });

      if (data && Array.isArray(data.data) && data.data.length > 0) {
        const mapped: ServicePricingItem[] = data.data.map((raw: BackendRawItem) => ({
          id: `PRC-${raw.id}`,
          code: raw.serviceCode,
          name: raw.name,
          description: raw.description ?? '',
          category: raw.category || 'Khám Lâm sàng',
          department: raw.departmentName || 'Khoa Khám bệnh',
          currentPrice: raw.price,
          formattedPrice: raw.priceFormatted || `${raw.price.toLocaleString('vi-VN')} đ`,
          effectiveDate: '2026-01-01',
          decisionNumber: 'QĐ-142/BV-KH',
          status: raw.isActive ? 'ACTIVE' : 'SUSPENDED',
          onlineBooking: Boolean(raw.allowsOnlineBooking),
          priceHistory: [
            {
              date: '2026-01-01',
              price: raw.price,
              decisionNumber: 'QĐ-142/BV-KH',
              note: 'Biểu phí hiện hành áp dụng năm 2026',
              appliedBy: 'Hội đồng Giá Bệnh viện',
            },
          ],
        }));

        const filtered = filterPricingItems(mapped, filters);
        return {
          data: filtered,
          total: data.total || filtered.length,
          page: data.page || page,
          pageSize: data.pageSize || pageSize,
          totalPages: data.totalPages || Math.ceil(filtered.length / pageSize),
          metrics: calculateMetrics(filtered),
        };
      }
    } catch (error) {
      if (!isNetworkOrGatewayError(error)) throw error;
    }
  }

  const filtered = filterPricingItems(INITIAL_PRICING_DATA, filters);
  const total = filtered.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const startIndex = (safePage - 1) * pageSize;

  return {
    data: filtered.slice(startIndex, startIndex + pageSize),
    total,
    page: safePage,
    pageSize,
    totalPages,
    metrics: calculateMetrics(filtered),
  };
}

export async function updateServicePrice(
  id: string,
  payload: UpdateServicePricePayload,
): Promise<ServicePricingItem> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    const numericId = id.replace(/\D/g, '');
    if (numericId) {
      try {
        await apiClient.put(`/admin/examination-services/${numericId}`, {
          price: payload.newPrice,
          status: payload.status,
        });
      } catch (error) {
        if (!isNetworkOrGatewayError(error)) throw error;
      }
    }
  }

  const updated = updateMockServicePrice(id, payload);
  if (!updated) throw new Error(`Không tìm thấy dịch vụ ${id}`);
  return updated;
}
