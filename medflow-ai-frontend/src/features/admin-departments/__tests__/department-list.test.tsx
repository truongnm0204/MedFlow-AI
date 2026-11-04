import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { DEPARTMENT_MOCK_DATA } from '../data/department-mock-data';
import { DepartmentListPage } from '../pages/department-list-page';
import { useDepartmentStore } from '../store/department.store';

const renderPage = () =>
  render(
    <MemoryRouter>
      <DepartmentListPage />
    </MemoryRouter>,
  );
describe('DepartmentListPage (SCR-ADM-DEP-01)', () => {
  beforeEach(() => {
    useDepartmentStore.getState().resetToMock();
  });

  it('has 14 mock departments with 12 active', () => {
    expect(DEPARTMENT_MOCK_DATA).toHaveLength(14);
    expect(DEPARTMENT_MOCK_DATA.filter((d) => d.status === 'ACTIVE')).toHaveLength(12);
  });

  it('renders breadcrumb and the 4 metric cards', () => {
    renderPage();
    expect(screen.getByText('Danh mục Khoa phòng (SCR-ADM-DEP-01)')).toBeInTheDocument();
    expect(screen.getByText('TỔNG SỐ KHOA')).toBeInTheDocument();
    expect(screen.getByText('14')).toBeInTheDocument();
    expect(screen.getByText('12 khoa đang hoạt động')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TẠM NGỪNG')).toBeInTheDocument();
    expect(screen.getByText('02')).toBeInTheDocument();
    expect(screen.getByText('CƠ SỞ QUẢN LÝ')).toBeInTheDocument();
    expect(screen.getByText('01')).toBeInTheDocument();
  });

  it('renders table columns and first page of rows', () => {
    renderPage();
    for (const col of ['MÃ KHOA', 'TÊN KHOA', 'CƠ SỞ', 'TRẠNG THÁI', 'THAO TÁC']) {
      expect(screen.getByRole('columnheader', { name: col })).toBeInTheDocument();
    }
    expect(screen.getByText('DEP-001')).toBeInTheDocument();
    expect(screen.getByText('Khoa Nội Tiêu hóa')).toBeInTheDocument();
    expect(screen.queryByText('DEP-011')).not.toBeInTheDocument();
  });

  it('paginates to the second page', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('DEP-014')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();
  });

  it('filters by search text', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm theo mã khoa hoặc tên khoa'), {
      target: { value: 'dep-003' },
    });
    expect(screen.getByText('Khoa Nội tiết')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();
  });

  it('filters by status', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'INACTIVE' },
    });
    expect(screen.getAllByText('Tạm ngừng', { selector: 'span' })).toHaveLength(2);
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();
  });

  it('shows empty state when nothing matches', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm theo mã khoa hoặc tên khoa'), {
      target: { value: 'zzz' },
    });
    expect(screen.getByText('Không tìm thấy khoa phòng phù hợp')).toBeInTheDocument();
  });

  it('opens deactivate modal when clicking power icon on an active department', () => {
    renderPage();
    const deactivateBtn = screen.getByRole('button', {
      name: 'Ngừng hoạt động Khoa Nội Tiêu hóa',
    });
    fireEvent.click(deactivateBtn);

    expect(
      screen.getByRole('heading', { name: 'Ngừng hoạt động khoa phòng?' }),
    ).toBeInTheDocument();
    expect(screen.getAllByText('DEP-001').length).toBeGreaterThanOrEqual(1);
  });

  it('successfully deactivates an eligible department and updates metrics and status', async () => {
    renderPage();
    // DEP-003 Khoa Nội tiết is active with 0 dependencies
    const deactivateBtn = screen.getByRole('button', {
      name: 'Ngừng hoạt động Khoa Nội tiết',
    });
    fireEvent.click(deactivateBtn);

    const modalConfirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(modalConfirmBtn).not.toBeDisabled();
    fireEvent.click(modalConfirmBtn);

    await waitFor(() => {
      expect(screen.getByText(/Đã ngừng hoạt động khoa phòng Khoa Nội tiết/i)).toBeInTheDocument();
    });
    // Status changed: 11 active, 3 inactive
    expect(screen.getByText('11 khoa đang hoạt động')).toBeInTheDocument();
    expect(screen.getByText('03')).toBeInTheDocument();
  });

  it('reactivates an inactive department when clicking reactivate button', async () => {
    renderPage();
    // Go to page 2 where DEP-013 Khoa Da liễu is located
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));

    const reactivateBtn = screen.getByRole('button', {
      name: 'Kích hoạt lại Khoa Da liễu',
    });
    fireEvent.click(reactivateBtn);

    await waitFor(() => {
      expect(
        screen.getByText(/Đã kích hoạt lại hoạt động cho khoa phòng Khoa Da liễu/i),
      ).toBeInTheDocument();
    });
    // Status changed: 13 active, 1 inactive
    expect(screen.getByText('13 khoa đang hoạt động')).toBeInTheDocument();
    expect(screen.getAllByText('01')).toHaveLength(2);
  });
});
