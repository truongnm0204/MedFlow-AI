import { fireEvent, render, screen } from '@testing-library/react';
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

describe('ServicePricingPage (UC52 / SCR-ADM-PRC-01 - View Service Pricing)', () => {
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

    expect(
      screen.getByRole('heading', { name: /Bảng Giá & Biểu Phí Dịch Vụ Y Tế/i }),
    ).toBeInTheDocument();
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

  it('does not render update price or price history buttons in view screen', () => {
    renderPricingPage();

    expect(screen.queryByRole('button', { name: /Cập nhật giá/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Lịch sử giá/i })).not.toBeInTheDocument();
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
