import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchExaminationServicesApi, updateServicePriceApi } from '../api/examination-service.api';
import { INITIAL_SERVICES } from '../data/examination-services-data';

describe('Examination Service Pricing API (M3-207)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchExaminationServicesApi falls back to initial services when offline or in test mode', async () => {
    const services = await fetchExaminationServicesApi();
    expect(services).toHaveLength(INITIAL_SERVICES.length);
    expect(services[0].code).toBe('EX-1-01');
  });

  it('updateServicePriceApi updates price and records history in fallback mode', async () => {
    const updated = await updateServicePriceApi('EX-1-01', {
      price: 380000,
      decisionNumber: 'QĐ-999/BV-KH',
      effectiveDate: '2026-06-01',
      appliedBy: 'Hội đồng Giá Bệnh viện',
      note: 'Tăng giá thử nghiệm',
    });

    expect(updated.price).toBe(380000);
    expect(updated.decisionNumber).toBe('QĐ-999/BV-KH');
    expect(updated.effectiveDate).toBe('2026-06-01');
    expect(updated.priceHistory?.[0].price).toBe(380000);
    expect(updated.priceHistory?.[0].decisionNumber).toBe('QĐ-999/BV-KH');
  });

  it('updateServicePriceApi calls PUT /admin/examination-services/:id when HTTP api is mocked', async () => {
    const putMock = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        id: '1',
        code: 'EX-1-01',
        name: 'Khám Nội Tổng quát',
        price: 420000,
        priceFormatted: '420.000 đ',
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
  });
});
