import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { MedicinePricePage } from '../pages/medicine-price-page';
import { useMedicinePriceStore } from '../store/medicine-price.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

const MOCK_MEDICINES = [
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
    priceHistory: [],
  },
  {
    id: 12,
    medicineCode: 'MED-002',
    name: 'Amoxicillin 250mg',
    activeIngredient: 'Amoxicillin',
    strength: '250mg',
    dispensingUnit: 'Viên',
    facility: 'MedFlow CS1',
    isActive: true,
    status: 'ACTIVE',
    price: 0,
    hasEffectivePrice: false,
    priceFormatted: '0 đ',
    priceHistory: [],
  },
];

describe('MedicinePricePage (GH-865 GET /admin/medicine-prices)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 1,
        fullName: 'Quản trị Bệnh viện',
        email: 'admin@medflow.vn',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
    });
    useMedicinePriceStore.setState({
      items: [],
      priceHistoryMedicine: null,
      updatePriceMedicine: null,
    });
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        hospitalId: 1,
        hospitalName: 'MedFlow CS1',
        data: MOCK_MEDICINES,
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
      },
    });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/medicine-prices']}>
          <MedicinePricePage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('fetches GET /admin/medicine-prices and renders medicines, price and KPI metrics', async () => {
    renderPage();

    expect(await screen.findByText('Paracetamol 500mg')).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/admin/medicine-prices',
      expect.objectContaining({ params: { page: 1, pageSize: 200 } }),
    );
    expect(screen.getAllByText('1.500 đ').length).toBeGreaterThan(0);
    expect(screen.getByText('TỔNG THUỐC ĐỊNH GIÁ')).toBeInTheDocument();
    expect(screen.getByText(/thuốc đã áp dụng giá/)).toBeInTheDocument();
    expect(screen.getByText('Amoxicillin 250mg')).toBeInTheDocument();
    expect(screen.getAllByText('Chưa có giá').length).toBeGreaterThan(0);
  });

  it('filters by keyword and opens the update price modal', async () => {
    renderPage();
    expect(await screen.findByText('Paracetamol 500mg')).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText(/Tìm mã, tên hoặc hoạt chất thuốc/i), {
      target: { value: 'MED-001' },
    });
    expect(screen.queryByText('Amoxicillin 250mg')).not.toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: /Cập nhật giá/i })[0]);
    expect(screen.getByText('Cập nhật giá thuốc')).toBeInTheDocument();
  });
});
