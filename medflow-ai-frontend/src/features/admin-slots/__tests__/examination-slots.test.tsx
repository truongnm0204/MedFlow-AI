import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { INITIAL_EXAMINATION_SLOTS } from '../data/examination-slots-mock';
import { ExaminationSlotListPage } from '../pages/examination-slot-list-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';

describe('ExaminationSlotListPage (SCR-ADM-SLT-01)', () => {
  beforeEach(() => {
    useExaminationSlotStore.setState({
      slots: [...INITIAL_EXAMINATION_SLOTS],
      searchQuery: '',
      dateFilter: '',
      specialtyFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 20,
      selectedIds: [],
    });
  });

  const renderPage = () =>
    render(
      <MemoryRouter>
        <ExaminationSlotListPage />
      </MemoryRouter>,
    );

  it('renders header, breadcrumbs, and active navigation tab for slot management', () => {
    renderPage();
    expect(screen.getAllByText(/MedFlow/i).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('HOSPITAL ADMIN')).toBeInTheDocument();
    expect(screen.getByText('Quản lý khung giờ khám (SCR-ADM-SLT-01)')).toBeInTheDocument();
    expect(screen.getAllByText('Quản lý Slot khám').length).toBeGreaterThanOrEqual(1);
  });

  it('displays the 4 metric cards matching Figma specifications', () => {
    renderPage();
    expect(screen.getByText('SLOT ĐANG MỞ')).toBeInTheDocument();
    expect(screen.getByText('Đủ điều kiện nhận đặt lịch')).toBeInTheDocument();

    expect(screen.getByText('CÓ LƯỢT ĐẶT')).toBeInTheDocument();
    expect(screen.getByText('Lượt đặt được giữ nguyên')).toBeInTheDocument();

    expect(screen.getByText('SẮP ĐẾN GIỜ')).toBeInTheDocument();
    expect(screen.getByText('Trong 7 ngày tới')).toBeInTheDocument();

    expect(screen.getByText('CẦN RÀ SOÁT')).toBeInTheDocument();
    expect(screen.getByText('Tài nguyên hoặc sức chứa')).toBeInTheDocument();
  });

  it('renders table columns and first page slot items', () => {
    renderPage();
    expect(screen.getByText('MÃ SLOT')).toBeInTheDocument();
    expect(screen.getByText('LỊCH / BÁC SĨ')).toBeInTheDocument();
    expect(screen.getByText('LOẠI DỊCH VỤ')).toBeInTheDocument();
    expect(screen.getByText('CHUYÊN KHOA')).toBeInTheDocument();
    expect(screen.getByText('PHÒNG')).toBeInTheDocument();
    expect(screen.getByText('SỨC CHỨA')).toBeInTheDocument();
    expect(screen.getByText('NGÀY / GIỜ')).toBeInTheDocument();
    expect(screen.getByText('TRẠNG THÁI')).toBeInTheDocument();
    expect(screen.getByText('THAO TÁC')).toBeInTheDocument();

    // Check specific rows from Figma
    expect(screen.getByText('SLT-001')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Nguyễn Thu Lan').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('PK-102').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('04 / 08').length).toBeGreaterThanOrEqual(1);

    expect(screen.getByText('SLT-002')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Trần Hoài Nam').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('PK-108').length).toBeGreaterThanOrEqual(1);
  });

  it('filters slot items by search query', () => {
    renderPage();
    const searchInput = screen.getByLabelText('Tìm kiếm khung giờ khám');
    fireEvent.change(searchInput, { target: { value: 'SLT-002' } });

    expect(screen.getByText('SLT-002')).toBeInTheDocument();
    expect(screen.getByText('BS. Trần Hoài Nam')).toBeInTheDocument();
    expect(screen.queryByText('SLT-001')).not.toBeInTheDocument();
  });

  it('filters slot items by specialty dropdown', () => {
    renderPage();
    const specialtySelect = screen.getByLabelText('Lọc theo chuyên khoa');
    fireEvent.change(specialtySelect, { target: { value: 'Tim mạch' } });

    expect(screen.getAllByText('BS. Trần Hoài Nam').length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText('BS. Nguyễn Thu Lan')).not.toBeInTheDocument();
  });

  it('filters slot items by status dropdown', () => {
    renderPage();
    const statusSelect = screen.getByLabelText('Lọc theo trạng thái');
    fireEvent.change(statusSelect, { target: { value: 'CLOSED' } });

    expect(screen.getByText('SLT-008')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Đặng Hoàng Mai').length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText('SLT-001')).not.toBeInTheDocument();
  });

  it('supports selecting rows and batch status update', () => {
    renderPage();
    const selectAllCheckbox = screen.getByLabelText('Chọn tất cả khung giờ trên trang');
    fireEvent.click(selectAllCheckbox);

    expect(screen.getByText(/Đã chọn/i)).toBeInTheDocument();
    const batchCloseBtn = screen.getByRole('button', { name: 'Đóng hàng loạt' });
    fireEvent.click(batchCloseBtn);

    // After closing batch, slots should reflect closed
    expect(screen.getAllByText('Đã đóng').length).toBeGreaterThan(0);
  });

  it('handles pagination navigation and page size change', () => {
    renderPage();
    expect(screen.getByText(/Hiển thị/i)).toBeInTheDocument();

    const pageSizeSelect = screen.getByLabelText('Số dòng trên mỗi trang');
    fireEvent.change(pageSizeSelect, { target: { value: '8' } });

    expect(useExaminationSlotStore.getState().pageSize).toBe(8);
  });

  it('triggers CSV export without throwing', () => {
    renderPage();
    const createObjectURLMock = vi.fn().mockReturnValue('blob:test');
    const revokeObjectURLMock = vi.fn();
    window.URL.createObjectURL = createObjectURLMock;
    window.URL.revokeObjectURL = revokeObjectURLMock;

    const exportBtn = screen.getByRole('button', { name: /Xuất Excel/i });
    expect(exportBtn).toBeInTheDocument();
    fireEvent.click(exportBtn);

    expect(createObjectURLMock).toHaveBeenCalled();
  });
});
