import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { ExaminationServiceListPage } from '../pages/examination-service-list-page';
import { useExaminationServiceStore } from '../store/examination-service.store';
import { INITIAL_SERVICES } from '../data/examination-services-data';

describe('ExaminationServiceListPage (SCR-ADM-ESV-01)', () => {
  beforeEach(() => {
    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      searchQuery: '',
      categoryFilter: '',
      departmentFilter: '',
      bookingFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 10,
      selectedIds: [],
      isColumnModalOpen: false,
      priceHistoryService: null,
    });
  });

  const renderPage = () =>
    render(
      <MemoryRouter>
        <ExaminationServiceListPage />
      </MemoryRouter>,
    );

  it('renders header, breadcrumbs, and active navigation tab', () => {
    renderPage();
    expect(screen.getAllByText(/MedFlow/i).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('HOSPITAL ADMIN')).toBeInTheDocument();
    expect(
      screen.getByText('Danh mục Dịch vụ Khám Ngoại trú (SCR-ADM-ESV-01)'),
    ).toBeInTheDocument();
    expect(screen.getByText('Danh mục Dịch vụ & Kỹ thuật')).toBeInTheDocument();
  });

  it('displays the 4 metric cards matching Figma specifications', () => {
    renderPage();
    expect(screen.getByText('TỔNG SỐ DỊCH VỤ KHÁM')).toBeInTheDocument();
    expect(screen.getAllByText('32').length).toBeGreaterThanOrEqual(1);

    expect(screen.getByText('MỞ ĐẶT HẸN ONLINE')).toBeInTheDocument();
    expect(screen.getByText('28')).toBeInTheDocument();
    expect(screen.getByText(/87.5% Sẵn sàng trên Cổng BN/i)).toBeInTheDocument();

    expect(screen.getByText('GIÁ HIỆU LỰC TẠI CƠ SỞ')).toBeInTheDocument();
    expect(screen.getByText('Theo cơ sở')).toBeInTheDocument();
    expect(screen.getByText(/Chỉ xem - Quản lý tại UC52/i)).toBeInTheDocument();

    expect(screen.getByText('TẠM NGỪNG DỊCH VỤ')).toBeInTheDocument();
    expect(screen.getByText('04')).toBeInTheDocument();
  });

  it('renders the examination service table with first page items', () => {
    renderPage();
    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).toBeInTheDocument();
    expect(screen.getByText('300.000 đ')).toBeInTheDocument();
    expect(screen.getByText('EX-2-08')).toBeInTheDocument();
    expect(
      screen.getByText('Hội chẩn Đa chuyên khoa Ca bệnh Phức tạp Ngoại trú'),
    ).toBeInTheDocument();
  });

  it('filters services when typing in the search box', () => {
    renderPage();
    const searchInput = screen.getByLabelText('Tìm dịch vụ khám');
    fireEvent.change(searchInput, { target: { value: 'Tim mạch' } });

    expect(screen.getByText('Khám Tim mạch & Đo Điện tâm đồ Chuyên sâu')).toBeInTheDocument();
    expect(screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).not.toBeInTheDocument();
  });

  it('filters services by category', () => {
    renderPage();
    const categorySelect = screen.getByLabelText('Lọc phân loại');
    fireEvent.change(categorySelect, { target: { value: 'Hội chẩn Lâm sàng' } });

    expect(
      screen.getByText('Hội chẩn Đa chuyên khoa Ca bệnh Phức tạp Ngoại trú'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).not.toBeInTheDocument();
  });

  it('filters services by department', () => {
    renderPage();
    const departmentSelect = screen.getByLabelText('Lọc khoa phụ trách');
    fireEvent.change(departmentSelect, {
      target: { value: 'Khoa Hô hấp & Dị ứng' },
    });

    expect(
      screen.getByText('Khám Chuyên khoa Hô hấp & Đánh giá Chức năng Phổi'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).not.toBeInTheDocument();
  });

  it('toggles service status from ACTIVE to SUSPENDED', () => {
    renderPage();
    const toggleBtn = screen.getByLabelText('Đổi trạng thái Khám Nội Tổng quát & Tư vấn Sức khỏe');
    fireEvent.click(toggleBtn);

    const updated = useExaminationServiceStore.getState().services.find((s) => s.id === 'EX-1-01');
    expect(updated?.status).toBe('SUSPENDED');
  });

  it('opens and closes the column customization modal', () => {
    renderPage();
    const columnBtn = screen.getByTitle('Tùy chỉnh cột hiển thị');
    fireEvent.click(columnBtn);

    expect(screen.getByText('Tùy Chỉnh Cột Hiển Thị')).toBeInTheDocument();
    const closeBtn = screen.getByText('Áp Dụng & Đóng');
    fireEvent.click(closeBtn);

    expect(screen.queryByText('Tùy Chỉnh Cột Hiển Thị')).not.toBeInTheDocument();
  });

  it('opens price history modal for a service', () => {
    renderPage();
    const historyBtn = screen.getByLabelText(
      'Xem lịch sử giá Khám Nội Tổng quát & Tư vấn Sức khỏe',
    );
    fireEvent.click(historyBtn);

    expect(screen.getByText('Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52')).toBeInTheDocument();
    expect(screen.getByText(/Quy tắc liên kết UC52/i)).toBeInTheDocument();
  });

  it('links to the separate create service page from the Add button', () => {
    renderPage();
    const addLink = screen.getByRole('link', { name: /Thêm Dịch Vụ Mới/i });
    expect(addLink).toHaveAttribute('href', '/admin/services/new');
  });

  it('links to the separate edit service page from the edit action', () => {
    renderPage();
    const editLink = screen.getByLabelText('Chỉnh sửa Khám Nội Tổng quát & Tư vấn Sức khỏe');
    expect(editLink).toHaveAttribute('href', '/admin/services/EX-1-01/edit');
  });

  it('supports selecting rows and batch actions', () => {
    renderPage();
    const rowCheckbox = screen.getByLabelText('Chọn Khám Nội Tổng quát & Tư vấn Sức khỏe');
    fireEvent.click(rowCheckbox);

    expect(screen.getByText(/Đã chọn/i)).toBeInTheDocument();
    const suspendBatchBtn = screen.getByText('Tạm dừng hàng loạt');
    fireEvent.click(suspendBatchBtn);

    const updated = useExaminationServiceStore.getState().services.find((s) => s.id === 'EX-1-01');
    expect(updated?.status).toBe('SUSPENDED');
  });

  it('navigates pagination correctly', () => {
    renderPage();
    expect(screen.getByText(/trong tổng số/i)).toBeInTheDocument();
    const nextBtn = screen.getByLabelText('Trang tiếp');
    fireEvent.click(nextBtn);

    expect(screen.getByRole('button', { name: 'Trang 2' })).toHaveAttribute('aria-current', 'page');
  });
});
