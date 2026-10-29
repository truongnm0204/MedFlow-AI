import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { RoomListPage } from '../pages/room-list-page';

const renderPage = () =>
  render(
    <MemoryRouter>
      <RoomListPage />
    </MemoryRouter>,
  );

describe('RoomListPage (SCR-ADM-ROOM-01)', () => {
  it('renders breadcrumb and active tab', () => {
    renderPage();
    expect(
      screen.getByText('Danh mục Buồng khám & Kỹ thuật (SCR-ADM-ROOM-01)'),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Buồng khám & Kỹ thuật').length).toBeGreaterThanOrEqual(1);
  });

  it('shows 4 metric cards', () => {
    renderPage();
    expect(screen.getByText('TỔNG SỐ BUỒNG KHÁM')).toBeInTheDocument();
    expect(screen.getByText('24')).toBeInTheDocument();
    expect(screen.getByText('20 phòng đang hoạt động')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TẠM NGỪNG / BẢO TRÌ')).toBeInTheDocument();
    expect(screen.getByText('04')).toBeInTheDocument();
    expect(screen.getByText('KHOA PHÒNG LIÊN KẾT')).toBeInTheDocument();
    expect(screen.getByText('14')).toBeInTheDocument();
  });

  it('renders table columns and paginates 10 rows per page', () => {
    renderPage();
    for (const h of [
      'MÃ PHÒNG',
      'TÊN BUỒNG KHÁM',
      'KHOA PHÒNG',
      'VỊ TRÍ / TẦNG',
      'TRẠNG THÁI KHẢ DỤNG',
      'THAO TÁC',
    ]) {
      expect(screen.getByText(h)).toBeInTheDocument();
    }
    expect(screen.getAllByRole('row')).toHaveLength(11);
    expect(screen.getByText('RM-101')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Trang sau'));
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
    expect(screen.getByText('RM-301')).toBeInTheDocument();
  });

  it('filters by search text', () => {
    renderPage();
    fireEvent.change(screen.getByPlaceholderText('Tìm theo mã phòng hoặc tên phòng...'), {
      target: { value: 'rm-201' },
    });
    expect(screen.getByText('RM-201')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
  });

  it('filters by status and department', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'MAINTENANCE' },
    });
    expect(screen.getAllByRole('row')).toHaveLength(3);
    fireEvent.change(screen.getByLabelText('Lọc theo khoa phòng'), {
      target: { value: 'Khoa Ngoại' },
    });
    expect(screen.getByText('RM-305')).toBeInTheDocument();
    expect(screen.queryByText('RM-203')).not.toBeInTheDocument();
  });

  it('shows empty state when nothing matches', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tìm kiếm buồng khám'), { target: { value: 'zzz' } });
    expect(screen.getByText('Không tìm thấy buồng khám phù hợp.')).toBeInTheDocument();
  });

  it('exposes export and add buttons', () => {
    renderPage();
    expect(screen.getByRole('button', { name: 'Xuất Excel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Thêm buồng khám' })).toBeInTheDocument();
  });
});
