import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { fetchExaminationServicesApi, updateServicePriceApi } from '../api/examination-service.api';

describe('Examination Service Pricing API (M3-207 real HTTP only)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchExaminationServicesApi calls GET /admin/examination-services and propagates network errors without mock fallback', async () => {
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({
        data: {
          data: [
            {
              id: 1,
              serviceCode: 'EX-1-01',
              name: 'Khám Nội Tổng quát',
              category: 'Khám Lâm sàng',
              departmentName: 'Khoa Khám bệnh',
              price: 300000,
              allowsOnlineBooking: true,
              isActive: true,
              status: 'ACTIVE',
            },
          ],
        },
      })
      .mockRejectedValueOnce(new Error('Network down'));

    const services = await fetchExaminationServicesApi();
    expect(getSpy).toHaveBeenCalledWith(
      '/admin/examination-services',
      expect.objectContaining({ params: { page: 1, pageSize: 200 } }),
    );
    expect(services).toHaveLength(1);
    expect(services[0].code).toBe('EX-1-01');

    await expect(fetchExaminationServicesApi()).rejects.toThrow('Network down');
  });

  it('updateServicePriceApi calls PUT /admin/examination-services/:id and records decision metadata', async () => {
    const putMock = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        id: 1,
        serviceCode: 'EX-1-01',
        name: 'Khám Nội Tổng quát',
        price: 420000,
        priceFormatted: '420.000 đ',
        isActive: true,
        status: 'ACTIVE',
      },
    });

    const result = await updateServicePriceApi('1', {
      price: 420000,
      decisionNumber: 'QĐ-777/BV-KH',
      effectiveDate: '2026-07-01',
      appliedBy: 'Ban Giám đốc',
      note: 'API test',
    });

    expect(putMock).toHaveBeenCalledWith('/admin/examination-services/1', {
      price: 420000,
    });
    expect(result.price).toBe(420000);
    expect(result.decisionNumber).toBe('QĐ-777/BV-KH');
    expect(result.priceHistory?.[0].price).toBe(420000);
  });
});
