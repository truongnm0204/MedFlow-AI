import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServicePriceUpdateModal } from '../components/service-price-update-modal';
import { INITIAL_SERVICES } from '../data/examination-services-data';
import { useExaminationServiceStore } from '../store/examination-service.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    put: vi.fn(),
  },
}));

describe('ServicePriceUpdateModal (M3-207 FE Update Service Price real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationServiceStore.setState({
      services: [{ ...INITIAL_SERVICES[0], id: '1' }],
      priceHistoryService: null,
      updatePriceService: null,
    });
    vi.mocked(apiClient.put).mockResolvedValue({
      data: {
        id: 1,
        serviceCode: INITIAL_SERVICES[0].code,
        name: INITIAL_SERVICES[0].name,
        category: INITIAL_SERVICES[0].category,
        departmentName: INITIAL_SERVICES[0].department,
        price: 450000,
        allowsOnlineBooking: true,
        isActive: true,
        status: 'ACTIVE',
      },
    });
  });

  const renderComponent = () =>
    render(
      <>
        <ServicePriceUpdateModal />
        <ServicePriceHistoryModal />
      </>,
    );

  it('does not render when updatePriceService is null and renders details when set', () => {
    const first = renderComponent();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    first.unmount();

    const testService = { ...INITIAL_SERVICES[0], id: '1' };
    useExaminationServiceStore.setState({ updatePriceService: testService });
    renderComponent();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Cập nhật Biểu phí Dịch vụ (UC52)')).toBeInTheDocument();
  });

  it('calculates live price difference, presets, validates zero price, and calls PUT /admin/examination-services/:id', async () => {
    const testService = { ...INITIAL_SERVICES[0], id: '1' };
    useExaminationServiceStore.setState({ updatePriceService: testService });
    renderComponent();

    fireEvent.click(screen.getByRole('button', { name: '+50k' }));
    expect((screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i) as HTMLInputElement).value).toBe(
      '350000',
    );

    fireEvent.change(screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i), {
      target: { value: '0' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i }));
    expect(await screen.findByText(/Mức giá dịch vụ phải lớn hơn 0 đ/i)).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i), {
      target: { value: '450000' },
    });
    fireEvent.change(screen.getByLabelText(/Số Quyết định \/ Căn cứ pháp lý/i), {
      target: { value: 'QĐ-888/BV-KH' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith('/admin/examination-services/1', {
        price: 450000,
      });
      const updated = useExaminationServiceStore.getState().services.find((s) => s.id === '1');
      expect(updated?.price).toBe(450000);
      expect(updated?.decisionNumber).toBe('QĐ-888/BV-KH');
    });
  });
});
