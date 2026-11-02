import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { DEPARTMENT_MOCK_DATA } from '../data/department-mock-data';
import { DepartmentListPage } from '../pages/department-list-page';

const renderPage = () =>
  render(
    <MemoryRouter>
      <DepartmentListPage />
    </MemoryRouter>,
  );

describe('DepartmentListPage (SCR-ADM-DEP-01)', () => {
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
});
