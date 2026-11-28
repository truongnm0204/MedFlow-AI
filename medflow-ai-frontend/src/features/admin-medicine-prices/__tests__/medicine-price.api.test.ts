import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  fetchAdminMedicinePricesApi,
  fetchMedicinePriceHistoryApi,
  updateMedicinePriceApi,
} from '../api/medicine-price.api';

describe('Medicine Price API (GH-865 real contract)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchAdminMedicinePricesApi calls GET /admin/medicine-prices and maps items + metrics', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        hospitalId: 1,
        hospitalName: 'MedFlow CS1',
        total: 2,
        page: 1,
        pageSize: 200,
        totalPages: 1,
        metrics: {
          totalMedicines: 2,
          pricedMedicinesCount: 1,
          pricedRatioPercent: 50,
          averagePrice: 1500,
          minPrice: 0,
          maxPrice: 1500,
          latestEffectiveDate: '2026-01-01',
        },
        data: [
          {
            id: 11,
            medicineCode: 'MED-001',
            name: 'Paracetamol 500mg',
            activeIngredient: 'Paracetamol',
            strength: '500mg',
            dosageForm: 'Viên nén',
            dispensingUnit: 'Viên',
            facility: 'MedFlow CS1',
            isActive: true,
            status: 'ACTIVE',
            price: 1500,
            currentPriceAmount: 1500,
            hasEffectivePrice: true,
            priceFormatted: '1.500 đ',
            validFrom: '2026-01-01',
            validTo: null,
            effectiveDate: '2026-01-01',
            decisionNumber: 'QĐ-865/BV-KH',
            appliedBy: 'Hội đồng Thuốc',
            priceHistory: [
              {
                id: 50,
                priceAmount: 1500,
                priceFormatted: '1.500 đ',
                validFrom: '2026-01-01',
                validTo: null,
                date: '2026-01-01',
                decisionNumber: 'QĐ-865/BV-KH',
                appliedBy: 'Hội đồng Thuốc',
                isActive: true,
                status: 'ACTIVE',
              },
            ],
          },
          {
            id: 12,
            medicineCode: 'MED-002',
            name: 'Amoxicillin 250mg',
            isActive: true,
            status: 'ACTIVE',
            price: 0,
            hasEffectivePrice: false,
            priceFormatted: '0 đ',
            priceHistory: [],
          },
        ],
      },
    });

    const result = await fetchAdminMedicinePricesApi();

    expect(getSpy).toHaveBeenCalledWith('/admin/medicine-prices', {
      params: { page: 1, pageSize: 200 },
    });
    expect(result.data).toHaveLength(2);
    expect(result.data[0].medicineCode).toBe('MED-001');
    expect(result.data[0].priceFormatted).toBe('1.500 đ');
    expect(result.data[0].priceHistory).toHaveLength(1);
    expect(result.metrics.totalMedicines).toBe(2);
    expect(result.metrics.pricedRatioPercent).toBe(50);
    expect(result.metrics.latestEffectiveDate).toBe('2026-01-01');
  });

  it('fetchMedicinePriceHistoryApi calls GET /admin/medicines/:idOrCode/prices/history', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        medicineId: 11,
        medicineCode: 'MED-001',
        name: 'Paracetamol 500mg',
        hospitalId: 1,
        priceHistory: [
          {
            id: 10,
            priceAmount: 1500,
            priceFormatted: '1.500 đ',
            validFrom: '2026-07-01',
            validTo: null,
            date: '2026-07-01',
            decisionNumber: 'QĐ-777/BV-KH',
            appliedBy: 'Ban Giám đốc',
            isActive: true,
            status: 'ACTIVE',
          },
        ],
      },
    });

    const history = await fetchMedicinePriceHistoryApi('MED-001');

    expect(getSpy).toHaveBeenCalledWith('/admin/medicines/MED-001/prices/history', {
      params: undefined,
    });
    expect(history).toHaveLength(1);
    expect(history[0].priceFormatted).toBe('1.500 đ');
    expect(history[0].decisionNumber).toBe('QĐ-777/BV-KH');
  });

  it('updateMedicinePriceApi calls PUT /admin/medicines/:idOrCode/prices with mapped body', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        id: 11,
        medicineCode: 'MED-001',
        name: 'Paracetamol 500mg',
        facility: 'MedFlow CS1',
        isActive: true,
        status: 'ACTIVE',
        price: 2000,
        currentPriceAmount: 2000,
        hasEffectivePrice: true,
        priceFormatted: '2.000 đ',
        validFrom: '2026-12-01',
        effectiveDate: '2026-12-01',
        priceHistory: [],
      },
    });

    const item = await updateMedicinePriceApi('MED-001', {
      medicineId: 11,
      medicineCode: 'MED-001',
      price: 2000,
      effectiveDate: '2026-12-01',
      validTo: '2027-12-31',
      decisionNumber: 'QĐ-999/BV-KH',
      appliedBy: 'Hội đồng Thuốc',
      note: 'Điều chỉnh giá thuốc',
    });

    expect(putSpy).toHaveBeenCalledWith('/admin/medicines/MED-001/prices', {
      priceAmount: 2000,
      price: 2000,
      validFrom: '2026-12-01',
      effectiveDate: '2026-12-01',
      validTo: '2027-12-31',
      decisionNumber: 'QĐ-999/BV-KH',
      appliedBy: 'Hội đồng Thuốc',
      note: 'Điều chỉnh giá thuốc',
      medicineId: 11,
      medicineCode: 'MED-001',
    });
    expect(item.price).toBe(2000);
    expect(item.hasEffectivePrice).toBe(true);
  });
});
