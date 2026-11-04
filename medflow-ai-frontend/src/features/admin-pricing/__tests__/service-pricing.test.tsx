import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import * as pricingApi from '../api/service-pricing.api';
import {
  calculateMetrics,
  INITIAL_PRICING_DATA,
  resetMockServicePricing,
} from '../data/service-pricing-mock';
import { ServicePricingPage } from '../pages/service-pricing-page';

function renderPricingPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        staleTime: 0,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/pricing']}>
        <ServicePricingPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ServicePricingPage (UC52 / SCR-ADM-PRC-01)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetMockServicePricing();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders page title, UC52 badge, breadcrumbs, and active navigation tab', async () => {
    renderPricingPage();

    expect(
      screen.getByRole('heading', { name: /Bảng Giá & Biểu Phí Dịch Vụ Y Tế/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('UC52')).toBeInTheDocument();
    expect(screen.getByText('Bảng giá & Biểu phí Dịch vụ (SCR-ADM-PRC-01)')).toBeInTheDocument();
    expect(screen.getByText('Bảng giá & Biểu phí')).toBeInTheDocument();
  });

  it('renders 4 KPI stat cards with correct metrics', async () => {
    renderPricingPage();

    expect(screen.getByText('DỊCH VỤ NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('ĐANG ÁP DỤNG HIỆU LỰC')).toBeInTheDocument();
    expect(screen.getByText('TẠM DỪNG NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('MỨC GIÁ TRUNG BÌNH')).toBeInTheDocument();

    const expectedMetrics = calculateMetrics(INITIAL_PRICING_DATA);
    expect(
      screen.getByText(String(expectedMetrics.totalServices).padStart(2, '0')),
    ).toBeInTheDocument();
    expect(
      screen.getByText(String(expectedMetrics.activePricingCount).padStart(2, '0')),
    ).toBeInTheDocument();
  });

  it('displays service rows with codes, names, departments, prices, and status badges', async () => {
    renderPricingPage();

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    });

    expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu')).toBeInTheDocument();
    expect(screen.getAllByText('300.000 đ').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('QĐ-142/BV-KH').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Đang áp dụng').length).toBeGreaterThanOrEqual(1);
  });

  it('filters services by text search in code or name', async () => {
    renderPricingPage();

    const searchInput = screen.getByLabelText('Tìm kiếm dịch vụ và biểu phí');
    fireEvent.change(searchInput, { target: { value: 'Tiêu hóa' } });

    await waitFor(() => {
      expect(screen.getByText('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật')).toBeInTheDocument();
      expect(
        screen.queryByText('Khám Mắt Toàn diện & Đo Khúc xạ Máy Tự động'),
      ).not.toBeInTheDocument();
    });
  });

  it('filters services by department dropdown', async () => {
    renderPricingPage();

    const deptSelect = screen.getByLabelText('Khoa phòng phụ trách');
    fireEvent.change(deptSelect, {
      target: { value: 'Khoa Tim mạch Can thiệp' },
    });

    await waitFor(() => {
      expect(
        screen.getByText('Khám Tim mạch Can thiệp & Đo Điện tim Chuyên sâu'),
      ).toBeInTheDocument();
      expect(
        screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu'),
      ).not.toBeInTheDocument();
    });
  });

  it('filters services by category dropdown', async () => {
    renderPricingPage();

    const catSelect = screen.getByLabelText('Phân loại dịch vụ');
    fireEvent.change(catSelect, {
      target: { value: 'Cận lâm sàng kỹ thuật' },
    });

    await waitFor(() => {
      expect(screen.getByText('Siêu âm Doppler Tim & Mạch máu Chuyên sâu')).toBeInTheDocument();
      expect(
        screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu'),
      ).not.toBeInTheDocument();
    });
  });

  it('filters services by price range', async () => {
    renderPricingPage();

    const priceSelect = screen.getByLabelText('Khoảng giá');
    fireEvent.change(priceSelect, {
      target: { value: 'under-200k' },
    });

    await waitFor(() => {
      expect(
        screen.getByText('Ghi Điện tim đồ 12 Chuyển đạo Kỹ thuật số (Digital ECG)'),
      ).toBeInTheDocument();
      expect(screen.getAllByText('80.000 đ').length).toBeGreaterThanOrEqual(1);
      expect(
        screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu'),
      ).not.toBeInTheDocument();
    });
  });

  it('filters services by status dropdown (SUSPENDED)', async () => {
    renderPricingPage();

    const statusSelect = screen.getByLabelText('Trạng thái áp dụng');
    fireEvent.change(statusSelect, {
      target: { value: 'SUSPENDED' },
    });

    await waitFor(() => {
      expect(
        screen.getByText('Khám Sức khỏe Tiền Hôn nhân & Tư vấn Di truyền VIP'),
      ).toBeInTheDocument();
      expect(screen.getAllByText('Tạm dừng').length).toBeGreaterThanOrEqual(1);
      expect(
        screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu'),
      ).not.toBeInTheDocument();
    });
  });

  it('shows empty state when no services match, with working reset button', async () => {
    renderPricingPage();

    const searchInput = screen.getByLabelText('Tìm kiếm dịch vụ và biểu phí');
    fireEvent.change(searchInput, { target: { value: 'KHONG_CO_DICH_VU_NAO' } });

    await waitFor(() => {
      expect(screen.getByText('Không tìm thấy dịch vụ nào phù hợp')).toBeInTheDocument();
    });

    const resetBtn = screen.getByRole('button', { name: 'Xóa toàn bộ bộ lọc' });
    fireEvent.click(resetBtn);

    await waitFor(() => {
      expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu')).toBeInTheDocument();
    });
  });

  it('opens and closes the price history modal with chronological price adjustments', async () => {
    renderPricingPage();

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    });

    const historyBtn = screen.getByRole('button', {
      name: 'Xem lịch sử giá của Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu',
    });
    fireEvent.click(historyBtn);

    expect(screen.getByText('Lịch Sử Biểu Phí & Quyết Định Áp Dụng (UC52)')).toBeInTheDocument();

    const closeBtn = screen.getByRole('button', {
      name: 'Đóng hộp thoại lịch sử giá',
    });
    fireEvent.click(closeBtn);

    await waitFor(() => {
      expect(
        screen.queryByText('Lịch Sử Biểu Phí & Quyết Định Áp Dụng (UC52)'),
      ).not.toBeInTheDocument();
    });
  });

  it('triggers CSV export when clicking Xuất CSV', async () => {
    renderPricingPage();

    const createObjectURLMock = vi.fn().mockReturnValue('blob:mock-url');
    const revokeObjectURLMock = vi.fn();
    window.URL.createObjectURL = createObjectURLMock;
    window.URL.revokeObjectURL = revokeObjectURLMock;

    const exportBtn = screen.getByRole('button', { name: /Xuất CSV/i });
    fireEvent.click(exportBtn);

    expect(createObjectURLMock).toHaveBeenCalled();
  });

  it('triggers print when clicking In biểu phí', async () => {
    const printMock = vi.spyOn(window, 'print').mockImplementation(() => {});
    renderPricingPage();

    const printBtn = screen.getByRole('button', { name: /In biểu phí/i });
    fireEvent.click(printBtn);

    expect(printMock).toHaveBeenCalled();
  });

  it('opens and submits the update price modal with new price and decision number', async () => {
    renderPricingPage();

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    });

    const updateBtn = screen.getByRole('button', {
      name: 'Cập nhật giá của Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu',
    });
    fireEvent.click(updateBtn);

    expect(screen.getByText('Cập Nhật Mức Giá & Quyết Định Áp Dụng')).toBeInTheDocument();

    const priceInput = screen.getByPlaceholderText('Nhập đơn giá mới...');
    fireEvent.change(priceInput, { target: { value: '350000' } });

    const decisionInput = screen.getByPlaceholderText('Ví dụ: QĐ-145/BV-KH');
    fireEvent.change(decisionInput, { target: { value: 'QĐ-999/BV-KH' } });

    const confirmBtn = screen.getByRole('button', {
      name: 'Xác nhận cập nhật giá',
    });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(
        screen.getByText(/Đã cập nhật mức giá mới 350\.000 đ theo QĐ-999\/BV-KH thành công/i),
      ).toBeInTheDocument();
    });
  });

  it('allows quick percentage adjustment chips (+10%) in update price modal', async () => {
    renderPricingPage();

    await waitFor(() => {
      expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    });

    const updateBtn = screen.getByRole('button', {
      name: 'Cập nhật giá của Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu',
    });
    fireEvent.click(updateBtn);

    const plus10Chip = screen.getByRole('button', { name: '+10%' });
    fireEvent.click(plus10Chip);

    const priceInput = screen.getByPlaceholderText('Nhập đơn giá mới...') as HTMLInputElement;
    expect(Number(priceInput.value)).toBe(330000);

    const cancelBtn = screen.getByRole('button', { name: 'Hủy bỏ' });
    fireEvent.click(cancelBtn);

    await waitFor(() => {
      expect(screen.queryByText('Cập Nhật Mức Giá & Quyết Định Áp Dụng')).not.toBeInTheDocument();
    });
  });

  it('displays error notice with retry button when query fails', async () => {
    vi.spyOn(pricingApi, 'fetchServicePricing').mockRejectedValueOnce(
      new Error('Máy chủ đang bảo trì'),
    );

    renderPricingPage();

    await waitFor(() => {
      expect(screen.getByText(/Không thể tải dữ liệu từ máy chủ/i)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /Thử lại/i })).toBeInTheDocument();
  });
});
