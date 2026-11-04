import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { RoleListPage } from '../pages/role-list-page';

const renderPage = () =>
  render(
    <MemoryRouter>
      <RoleListPage />
    </MemoryRouter>,
  );

describe('RoleListPage (SCR-ADM-ROLE-01)', () => {
  it('renders metric cards and table columns', () => {
    renderPage();
    expect(screen.getByText('TỔNG SỐ VAI TRÒ')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TÀI KHOẢN ĐƯỢC GÁN')).toBeInTheDocument();
    expect(screen.getByText('QUYỀN HẠT NHÂN')).toBeInTheDocument();
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('09')).toBeInTheDocument();
    expect(screen.getByText('14')).toBeInTheDocument();

    for (const column of [
      'MÃ VAI TRÒ',
      'TÊN VAI TRÒ / MÔ TẢ',
      'NHÓM PHÂN HỆ',
      'QUYỀN HẠN',
      'TÀI KHOẢN',
      'TRẠNG THÁI',
      'THAO TÁC',
    ]) {
      expect(screen.getByRole('columnheader', { name: column })).toBeInTheDocument();
    }

    expect(screen.getByText('SYSTEM_ADMIN')).toBeInTheDocument();
    expect(screen.getByText('DOCTOR')).toBeInTheDocument();
  });

  it('filters roles by search query', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm kiếm vai trò'), {
      target: { value: 'PHARMACY_STAFF' },
    });
    expect(screen.getByText('Nhân viên nhà thuốc')).toBeInTheDocument();
    expect(screen.queryByText('Bác sĩ lâm sàng')).not.toBeInTheDocument();
  });

  it('filters by scope and status and shows empty state', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Lọc theo nhóm vai trò'), {
      target: { value: 'SYSTEM' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'INACTIVE' },
    });
    expect(screen.getByText('SECURITY_AUDITOR')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Lọc theo nhóm vai trò'), {
      target: { value: 'PORTAL' },
    });
    expect(screen.getByText('Không tìm thấy vai trò hệ thống phù hợp.')).toBeInTheDocument();
  });

  it('paginates the role list', () => {
    renderPage();
    expect(screen.getByText('Trang 1 / 2')).toBeInTheDocument();
    expect(screen.queryByText('SECURITY_AUDITOR')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('SECURITY_AUDITOR')).toBeInTheDocument();
  });

  it('opens and closes role permission detail modal', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Xem chi tiết Bác sĩ lâm sàng' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getAllByText('MEDICAL_RECORD_WRITE').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Kê đơn thuốc điện tử')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Đóng'));
    expect(screen.queryByText('Kê đơn thuốc điện tử')).not.toBeInTheDocument();
  });
});
