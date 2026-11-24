import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import {
  fetchAdminServicePricesApi,
  fetchExaminationServicesApi,
  fetchServicePriceHistoryApi,
  updateServicePriceApi,
} from '../api/examination-service.api';

describe('Examination Service Pricing API (UC52 / GH-818 real contract)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchAdminServicePricesApi calls GET /admin/service-prices and maps history & metadata', async () => {
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
              hasEffectivePrice: true,
              effectiveDate: '2026-11-25',
              decisionNumber: 'QĐ-818/BV-KH',
              allowsOnlineBooking: true,
              isActive: true,
              status: 'ACTIVE',
              priceHistory: [
                {
                  id: 50,
                  date: '2026-11-25',
                  price: 300000,
                  decisionNumber: 'QĐ-818/BV-KH',
                  appliedBy: 'Hội đồng Giá',
                  note: 'Áp dụng biểu phí mới',
                  isActive: true,
                },
              ],
            },
          ],
        },
      })
      .mockRejectedValueOnce(new Error('Network down'));

    const services = await fetchAdminServicePricesApi();
    expect(getSpy).toHaveBeenCalledWith(
      '/admin/service-prices',
      expect.objectContaining({ params: { page: 1, pageSize: 200 } }),
    );
    expect(services).toHaveLength(1);
    expect(services[0].code).toBe('EX-1-01');
    expect(services[0].decisionNumber).toBe('QĐ-818/BV-KH');
    expect(services[0].priceHistory).toHaveLength(1);

    await expect(fetchAdminServicePricesApi()).rejects.toThrow('Network down');
  });

  it('fetchExaminationServicesApi calls GET /admin/examination-services without mock fallback', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        data: [
          {
            id: 2,
            serviceCode: 'EX-1-02',
            name: 'Khám Tim mạch',
            price: 400000,
            isActive: true,
          },
        ],
      },
    });

    const list = await fetchExaminationServicesApi();
    expect(getSpy).toHaveBeenCalledWith(
      '/admin/examination-services',
      expect.objectContaining({ params: { page: 1, pageSize: 100 } }),
    );
    expect(list[0].code).toBe('EX-1-02');
  });

  it('fetchServicePriceHistoryApi calls GET /admin/examination-services/:idOrCode/prices', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        serviceId: 1,
        serviceCode: 'EX-1-01',
        name: 'Khám Nội Tổng quát',
        serviceType: 'EXAMINATION',
        category: 'Khám Lâm sàng',
        facility: 'MedFlow CS1',
        isActive: true,
        status: 'ACTIVE',
        price: 420000,
        hasEffectivePrice: true,
        priceFormatted: '420.000 đ',
        priceHistory: [
          {
            id: 10,
            date: '2026-07-01',
            priceAmount: 420000,
            decisionNumber: 'QĐ-777/BV-KH',
            appliedBy: 'Ban Giám đốc',
            note: 'Điều chỉnh giá',
          },
        ],
      },
    });

    const history = await fetchServicePriceHistoryApi('EX-1-01');
    expect(getSpy).toHaveBeenCalledWith('/admin/examination-services/EX-1-01/prices');
    expect(history).toHaveLength(1);
    expect(history[0].decisionNumber).toBe('QĐ-777/BV-KH');
  });

  it('updateServicePriceApi calls PUT /admin/examination-services/:idOrCode/prices with full contract payload', async () => {
    const putMock = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        id: 1,
        serviceCode: 'EX-1-01',
        name: 'Khám Nội Tổng quát',
        price: 420000,
        currentPriceAmount: 420000,
        hasEffectivePrice: true,
        priceFormatted: '420.000 đ',
        effectiveDate: '2026-07-01',
        decisionNumber: 'QĐ-777/BV-KH',
        appliedBy: 'Ban Giám đốc',
        isActive: true,
        status: 'ACTIVE',
        priceHistory: [
          {
            id: 99,
            date: '2026-07-01',
            price: 420000,
            decisionNumber: 'QĐ-777/BV-KH',
            appliedBy: 'Ban Giám đốc',
            note: 'API test',
            isActive: true,
          },
        ],
      },
    });

    const result = await updateServicePriceApi('1', {
      price: 420000,
      decisionNumber: 'QĐ-777/BV-KH',
      effectiveDate: '2026-07-01',
      validTo: '2026-12-31',
      appliedBy: 'Ban Giám đốc',
      note: 'API test',
    });

    expect(putMock).toHaveBeenCalledWith('/admin/examination-services/1/prices', {
      priceAmount: 420000,
      price: 420000,
      validFrom: '2026-07-01',
      effectiveDate: '2026-07-01',
      validTo: '2026-12-31',
      decisionNumber: 'QĐ-777/BV-KH',
      appliedBy: 'Ban Giám đốc',
      note: 'API test',
    });
    expect(result.price).toBe(420000);
    expect(result.decisionNumber).toBe('QĐ-777/BV-KH');
    expect(result.priceHistory?.[0].price).toBe(420000);
  });
});
