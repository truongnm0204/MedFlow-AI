import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { ServiceEditModal } from '../pages/service-edit-modal';
import { useExaminationServiceStore } from '../store/examination-service.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

describe('ServiceEditModal (SCR-ADM-ESV-03 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationServiceStore.setState({
      services: [],
      priceHistoryService: null,
      updatePriceService: null,
      draft: null,
    });

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: [
          {
            id: 101,
            serviceCode: 'EX-1-01',
            name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe',
            description: 'Khám tổng quát',
            category: 'Khám Lâm sàng',
            departmentName: 'Khoa Khám bệnh',
            price: 300000,
            allowsOnlineBooking: true,
            isActive: true,
            status: 'ACTIVE',
          },
        ],
      },
    });
    vi.mocked(apiClient.put).mockImplementation(async (_url, body) => ({
      data: {
        id: 101,
        serviceCode: 'EX-1-01',
        name: (body as { name?: string }).name ?? 'Khám Nội Tổng quát & Tư vấn Sức khỏe',
        category: 'Khám Lâm sàng',
        departmentName: 'Khoa Khám bệnh',
        price: 300000,
        allowsOnlineBooking: true,
        isActive: true,
        status: 'ACTIVE',
      },
    }));
  });

  const renderModal = (serviceId = 'EX-1-01') => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={[`/admin/services/${serviceId}/edit`]}>
          <Routes>
            <Route path="/admin/services/:serviceId/edit" element={<ServiceEditModal />} />
            <Route path="/admin/services" element={<div>Danh mục Dịch vụ Page Mock</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('loads service from GET /admin/examination-services, renders fields, and handles not-found', async () => {
    const first = renderModal('EX-1-01');
    expect(
      await screen.findByRole('heading', {
        name: /Dịch vụ Khám - Khám Nội Tổng quát & Tư vấn Sức khỏe/i,
      }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Mã Dịch vụ khám \(Service ID\)/i)).toHaveValue('EX-1-01');
    first.unmount();

    renderModal('EX-99-999');
    expect(await screen.findByText('Không tìm thấy hồ sơ dịch vụ khám.')).toBeInTheDocument();
  });

  it('calls PUT /admin/examination-services/:id on save and opens UC52 modals', async () => {
    renderModal('EX-1-01');
    expect(
      await screen.findByDisplayValue('Khám Nội Tổng quát & Tư vấn Sức khỏe'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Lịch sử' }));
    expect(useExaminationServiceStore.getState().priceHistoryService?.code).toBe('EX-1-01');
    fireEvent.click(screen.getAllByRole('button', { name: 'Đóng' })[0]);

    fireEvent.click(screen.getByRole('button', { name: /Điều chỉnh giá/i }));
    expect(screen.getByText('Cập nhật Biểu phí Dịch vụ (UC52)')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Đóng modal cập nhật giá' }));

    fireEvent.change(screen.getByLabelText(/Tên Dịch vụ khám ngoại trú/i), {
      target: { value: 'Khám Nội Tổng quát & Tư vấn Chuyên sâu VIP' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu dịch vụ khám/i }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/examination-services/101',
        expect.objectContaining({
          name: 'Khám Nội Tổng quát & Tư vấn Chuyên sâu VIP',
        }),
      );
      expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
    });
  });
});
