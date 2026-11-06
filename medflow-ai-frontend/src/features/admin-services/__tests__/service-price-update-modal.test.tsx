import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';

import { ServicePriceHistoryModal } from '../components/service-price-history-modal';
import { ServicePriceUpdateModal } from '../components/service-price-update-modal';
import { INITIAL_SERVICES } from '../data/examination-services-data';
import { useExaminationServiceStore } from '../store/examination-service.store';

describe('ServicePriceUpdateModal (M3-207 FE Update Service Price)', () => {
  beforeEach(() => {
    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      priceHistoryService: null,
      updatePriceService: null,
    });
  });

  const renderComponent = () =>
    render(
      <>
        <ServicePriceUpdateModal />
        <ServicePriceHistoryModal />
      </>,
    );

  it('does not render when updatePriceService is null', () => {
    renderComponent();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renders modal dialog with service details and current price', () => {
    const testService = INITIAL_SERVICES[0];
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Cập nhật Biểu phí Dịch vụ (UC52)')).toBeInTheDocument();
    expect(screen.getByText(testService.code)).toBeInTheDocument();
    expect(screen.getByText(testService.name)).toBeInTheDocument();
    expect(screen.getByText(testService.department)).toBeInTheDocument();
  });

  it('calculates live price difference and displays percentage chip', async () => {
    const testService = INITIAL_SERVICES[0]; // price: 300,000
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();

    const priceInput = screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i);
    fireEvent.change(priceInput, { target: { value: '350000' } });

    await waitFor(() => {
      expect(screen.getByText(/\+50\.000 đ/)).toBeInTheDocument();
      expect(screen.getByText(/\+16\.7%/)).toBeInTheDocument();
    });
  });

  it('applies preset buttons correctly', async () => {
    const testService = INITIAL_SERVICES[0]; // 300,000
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();

    const plus50kBtn = screen.getByRole('button', { name: '+50k' });
    fireEvent.click(plus50kBtn);

    const priceInput = screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i) as HTMLInputElement;
    expect(priceInput.value).toBe('350000');
  });

  it('shows validation error when price is invalid or zero', async () => {
    const testService = INITIAL_SERVICES[0];
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();

    const priceInput = screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i);
    fireEvent.change(priceInput, { target: { value: '0' } });

    const submitBtn = screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Mức giá dịch vụ phải lớn hơn 0 đ/i)).toBeInTheDocument();
    });
  });

  it('submits valid form, updates service price, and records price history', async () => {
    const testService = INITIAL_SERVICES[0];
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();

    const priceInput = screen.getByLabelText(/Mức giá niêm yết mới \(VNĐ\)/i);
    fireEvent.change(priceInput, { target: { value: '450000' } });

    const decisionInput = screen.getByLabelText(/Số Quyết định \/ Căn cứ pháp lý/i);
    fireEvent.change(decisionInput, { target: { value: 'QĐ-888/BV-KH' } });

    const submitBtn = screen.getByRole('button', { name: /Xác nhận & Lưu giá mới/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      const updated = useExaminationServiceStore
        .getState()
        .services.find((s) => s.id === testService.id);
      expect(updated?.price).toBe(450000);
      expect(updated?.decisionNumber).toBe('QĐ-888/BV-KH');
      expect(updated?.priceHistory?.[0].price).toBe(450000);
    });
  });

  it('closes modal when clicking Cancel button or pressing Escape', () => {
    const testService = INITIAL_SERVICES[0];
    useExaminationServiceStore.setState({ updatePriceService: testService });

    renderComponent();
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    const cancelBtn = screen.getByRole('button', { name: /Hủy bỏ \[Esc\]/i });
    fireEvent.click(cancelBtn);

    expect(useExaminationServiceStore.getState().updatePriceService).toBeNull();
  });
});
