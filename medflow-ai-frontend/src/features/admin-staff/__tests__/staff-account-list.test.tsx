import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { StaffAccountListPage } from '../pages/staff-account-list-page';

const renderPage = () =>
  render(
    <MemoryRouter>
      <StaffAccountListPage />
    </MemoryRouter>,
  );

describe('StaffAccountListPage (SCR-ADM-STAFF-01)', () => {
  it('renders metric cards and table columns', () => {
    renderPage();
    expect(screen.getByText('TÀI KHOẢN NHÂN SỰ')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ KHÓA')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ VÔ HIỆU HÓA')).toBeInTheDocument();
    expect(screen.getByText('56')).toBeInTheDocument();
    expect(screen.getByText('05')).toBeInTheDocument();
    for (const column of ['MÃ NV', 'NHÂN SỰ / EMAIL', 'VAI TRÒ', 'KHOA CÔNG TÁC', 'CƠ SỞ', 'TRẠNG THÁI', 'THAO TÁC']) {
      expect(screen.getByRole('columnheader', { name: column })).toBeInTheDocument();
    }
    expect(screen.getByText('STF-0142')).toBeInTheDocument();
    expect(screen.getByText('minh.le@medflow.vn')).toBeInTheDocument();
  });

  it('filters by search query', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm kiếm tài khoản nhân sự'), {
      target: { value: 'STF-0089' },
    });
    expect(screen.getByText('Trần Hoài Nam')).toBeInTheDocument();
    expect(screen.queryByText('Lê Hoàng Minh')).not.toBeInTheDocument();
  });

  it('filters by role and status and shows empty state', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Lọc theo vai trò'), { target: { value: 'Nhân viên quầy' } });
    expect(screen.getByText('STF-0231')).toBeInTheDocument();
    expect(screen.queryByText('STF-0142')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), { target: { value: 'DISABLED' } });
    expect(screen.getByText('Không tìm thấy tài khoản nhân sự phù hợp.')).toBeInTheDocument();
  });

  it('paginates the list', () => {
    renderPage();
    expect(screen.getByText('Trang 1 / 2')).toBeInTheDocument();
    expect(screen.queryByText('STF-0219')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('STF-0219')).toBeInTheDocument();
  });
});
