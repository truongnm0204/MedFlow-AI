import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

describe('ServicePriceUpdateModal (UC52 / GH-818 real API)', () => {
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
        currentPriceAmount: 450000,
        hasEffectivePrice: true,
        effectiveDate: '2026-11-25',
        decisionNumber: 'QĐ-888/BV-KH',
        appliedBy: 'Hội đồng Giá Bệnh viện',
        allowsOnlineBooking: true,
        isActive: true,
        status: 'ACTIVE',
        priceHistory: [
          {
            id: 101,
            date: '2026-11-25',
            price: 450000,
            decisionNumber: 'QĐ-888/BV-KH',
            appliedBy: 'Hội đồng Giá Bệnh viện',
            note: 'Điều chỉnh mới',
            isActive: true,
          },
        ],
      },
    });
  });

  afterEach(() => {
    vi.useRealTimers();
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

  it('validates negative price, applies presets, and submits PUT /admin/examination-services/:id/prices', async () => {
    const testService = { ...INITIAL_SERVICES[0], id: '1' };
    useExaminationServiceStore.setState({ updatePriceService: testService });
    renderComponent();

    fireEvent.click(screen.getByRole('button', { name: '+50k' }));
    expect((screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i) as HTMLInputElement).value).toBe(
      '350000',
    );

    fireEvent.change(screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i), {
      target: { value: '-1000' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i }));
    expect(await screen.findByText(/Mức giá dịch vụ không được âm/i)).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i), {
      target: { value: '450000' },
    });
    fireEvent.change(screen.getByLabelText(/Số Quyết định \/ Căn cứ pháp lý/i), {
      target: { value: 'QĐ-888/BV-KH' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/examination-services/1/prices',
        expect.objectContaining({
          priceAmount: 450000,
          decisionNumber: 'QĐ-888/BV-KH',
        }),
      );
      const updated = useExaminationServiceStore.getState().services.find((s) => s.id === '1');
      expect(updated?.price).toBe(450000);
      expect(updated?.decisionNumber).toBe('QĐ-888/BV-KH');
    });
  });

  it('defaults effectiveDate to Asia/Ho_Chi_Minh date (2026-11-26) when UTC is 2026-11-25T17:30:00Z (00:30 VN)', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-25T17:30:00Z'));

    const testService = { ...INITIAL_SERVICES[0], id: '1' };
    useExaminationServiceStore.setState({ updatePriceService: testService });
    renderComponent();

    const effectiveDateInput = screen.getByLabelText(
      /Ngày bắt đầu có hiệu lực/i,
    ) as HTMLInputElement;
    expect(effectiveDateInput.value).toBe('2026-11-26');
  });
});
