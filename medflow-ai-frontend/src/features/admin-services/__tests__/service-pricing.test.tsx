import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import { ServicePricingPage } from '../pages/service-pricing-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

function renderPricingPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/pricing']}>
      <ServicePricingPage />
    </MemoryRouter>,
  );
}

describe('ServicePricingPage (UC52 / SCR-ADM-PRC-01)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationServiceStore.setState({
      services: JSON.parse(JSON.stringify(INITIAL_SERVICES)),
      priceHistoryService: null,
      page: 1,
      pageSize: 10,
    });
  });

  it('renders page title, UC52 badge, breadcrumbs, and active navigation tab', () => {
    renderPricingPage();

    expect(screen.getByRole('heading', { name: /Bảng Giá & Biểu Phí Dịch Vụ Y Tế/i })).toBeInTheDocument();
    expect(screen.getByText('UC52')).toBeInTheDocument();
    expect(screen.getByText('Bảng giá & Biểu phí Dịch vụ (SCR-ADM-PRC-01)')).toBeInTheDocument();
    expect(screen.getByText('Bảng giá & Biểu phí')).toBeInTheDocument();
  });

  it('displays the 4 KPI stat cards with correct metrics', () => {
    renderPricingPage();

    expect(screen.getByText('DỊCH VỤ NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('ĐANG ÁP DỤNG HIỆU LỰC')).toBeInTheDocument();
    expect(screen.getByText('TẠM DỪNG NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('MỨC GIÁ TRUNG BÌNH')).toBeInTheDocument();
  });

  it('displays service rows with codes, names, departments, prices, and status badges', () => {
    renderPricingPage();

    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).toBeInTheDocument();
    expect(screen.getAllByText('300.000 đ').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Đang áp dụng').length).toBeGreaterThanOrEqual(1);
  });

  it('filters services by text search in code or name', () => {
    renderPricingPage();

    const searchInput = screen.getByLabelText('Tìm kiếm dịch vụ và biểu phí');
    fireEvent.change(searchInput, { target: { value: 'Tiêu hóa' } });

    expect(screen.getByText('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật')).toBeInTheDocument();
    expect(screen.queryByText('Khám Mắt & Đo Khúc xạ Máy')).not.toBeInTheDocument();
  });

  it('filters services by department dropdown', () => {
    renderPricingPage();

    const deptSelect = screen.getByLabelText('Khoa phòng phụ trách');
    fireEvent.change(deptSelect, {
      target: { value: 'Khoa Nội Tiêu hóa' },
    });

    expect(screen.getByText('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật')).toBeInTheDocument();
  });

  it('opens and closes the price history modal with chronological price adjustments', async () => {
    renderPricingPage();

    const historyBtn = screen.getByRole('button', {
      name: 'Xem lịch sử giá của Khám Nội Tổng quát & Tư vấn Sức khỏe',
    });
    fireEvent.click(historyBtn);

    expect(screen.getByText('Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52')).toBeInTheDocument();

    const closeBtn = screen.getByLabelText('Đóng');
    fireEvent.click(closeBtn);

    await waitFor(() => {
      expect(screen.queryByText('Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52')).not.toBeInTheDocument();
    });
  });

  it('opens and submits the update price modal with new price and decision number', async () => {
    renderPricingPage();

    const updateBtn = screen.getByRole('button', {
      name: 'Cập nhật giá của Khám Nội Tổng quát & Tư vấn Sức khỏe',
    });
    fireEvent.click(updateBtn);

    expect(screen.getByText('Cập Nhật Mức Giá & Quyết Định Áp Dụng (UC52)')).toBeInTheDocument();

    const priceInput = screen.getByPlaceholderText('Nhập đơn giá mới...');
    fireEvent.change(priceInput, { target: { value: '380000' } });

    const decisionInput = screen.getByPlaceholderText('Ví dụ: QĐ-145/BV-KH');
    fireEvent.change(decisionInput, { target: { value: 'QĐ-888/BV-KH' } });

    const confirmBtn = screen.getByRole('button', { name: 'Xác nhận cập nhật giá' });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(screen.getByText(/Đã cập nhật mức giá mới 380\.000 đ theo QĐ-888\/BV-KH thành công/i)).toBeInTheDocument();
    });
  });

  it('triggers CSV export and print actions', () => {
    renderPricingPage();

    const createObjectURLMock = vi.fn().mockReturnValue('blob:mock-url');
    window.URL.createObjectURL = createObjectURLMock;
    window.URL.revokeObjectURL = vi.fn();
    const printMock = vi.spyOn(window, 'print').mockImplementation(() => {});

    const exportBtn = screen.getByRole('button', { name: /Xuất CSV/i });
    fireEvent.click(exportBtn);
    expect(createObjectURLMock).toHaveBeenCalled();

    const printBtn = screen.getByRole('button', { name: /In biểu phí/i });
    fireEvent.click(printBtn);
    expect(printMock).toHaveBeenCalled();
  });
});
