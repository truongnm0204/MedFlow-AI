import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import { ServicePricingPage } from '../pages/service-pricing-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

describe('ServicePricingPage (M3-205 & M3-207 UC52)', () => {
  beforeEach(() => {
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

    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      priceHistoryService: null,
      updatePriceService: null,
    });
  });

  const renderPage = () =>
    render(
      <MemoryRouter initialEntries={['/admin/pricing']}>
        <ServicePricingPage />
      </MemoryRouter>,
    );

  it('renders page header, UC52 badges, and 4 KPI metric cards', () => {
    renderPage();

    expect(
      screen.getByRole('heading', {
        name: /Bảng giá & Biểu phí Dịch vụ Khám Ngoại trú/i,
      }),
    ).toBeInTheDocument();
    expect(screen.getByText(/UC52 • Quản lý Biểu phí Cơ sở/i)).toBeInTheDocument();
    expect(screen.getByText('TỔNG DỊCH VỤ ĐỊNH GIÁ')).toBeInTheDocument();
    expect(screen.getByText('ĐƠN GIÁ TRUNG BÌNH')).toBeInTheDocument();
    expect(screen.getByText('KHOẢNG GIÁ NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('QUYẾT ĐỊNH GẦN NHẤT')).toBeInTheDocument();
  });

  it('renders table with service rows and formatted prices', () => {
    renderPage();

    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).toBeInTheDocument();
    expect(screen.getByText('300.000 đ')).toBeInTheDocument();
  });

  it('filters services by search query', async () => {
    renderPage();

    const searchInput = screen.getByPlaceholderText(/Tìm mã hoặc tên dịch vụ khám/i);
    fireEvent.change(searchInput, { target: { value: 'EX-1-01' } });

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
      expect(screen.queryByText('EX-2-02')).not.toBeInTheDocument();
    });
  });

  it('filters services by price range', async () => {
    renderPage();

    const rangeSelect = screen
      .getByLabelText(/Bộ lọc biểu phí/i)
      .querySelector('#pricing-range-filter');
    expect(rangeSelect).toBeInTheDocument();

    if (rangeSelect) {
      fireEvent.change(rangeSelect, { target: { value: 'above500' } });
      await waitFor(() => {
        expect(screen.getByText(/Hiển thị/i)).toBeInTheDocument();
      });
    }
  });

  it('resets filters when clicking reset button', async () => {
    renderPage();

    const searchInput = screen.getByPlaceholderText(/Tìm mã hoặc tên dịch vụ khám/i);
    fireEvent.change(searchInput, { target: { value: 'Dịch vụ không tồn tại XYZ' } });

    await waitFor(() => {
      expect(screen.getByText(/Không tìm thấy biểu phí dịch vụ/i)).toBeInTheDocument();
    });

    const resetButtons = screen.getAllByRole('button', { name: /Đặt lại bộ lọc/i });
    fireEvent.click(resetButtons[0]);

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    });
  });

  it('opens ServicePriceUpdateModal when clicking Cập nhật giá button', async () => {
    renderPage();

    const updateBtns = screen.getAllByRole('button', { name: /Cập nhật giá/i });
    fireEvent.click(updateBtns[0]);

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByText('Cập nhật Biểu phí Dịch vụ (UC52)')).toBeInTheDocument();
    });
  });

  it('opens ServicePriceHistoryModal when clicking Lịch sử button', async () => {
    renderPage();

    const historyBtns = screen.getAllByRole('button', { name: /Xem lịch sử giá/i });
    fireEvent.click(historyBtns[0]);

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByText(/Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52/i)).toBeInTheDocument();
    });
  });

  it('hides Cập nhật giá button for unauthorized users (DOCTOR)', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 99,
        fullName: 'Bác sĩ Khám',
        email: 'doctor@medflow.vn',
        roles: ['DOCTOR'],
        patientCode: null,
      },
    });

    renderPage();

    expect(screen.queryByRole('button', { name: /Cập nhật giá/i })).not.toBeInTheDocument();
    // But Lịch sử button is still available to view
    expect(screen.getAllByRole('button', { name: /Xem lịch sử giá/i }).length).toBeGreaterThan(0);
  });
});
