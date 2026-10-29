import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { RoomListPage } from '../pages/room-list-page';

function renderRoomPage() {
  useAuthStore.setState({
    user: {
      id: 1,
      email: 'admin@medflow.test',
      fullName: 'Quản trị viên',
      roles: ['HOSPITAL_ADMIN'],
      patientCode: null,
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <RoomListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RoomListPage (Issue #173)', () => {
  it('renders page header, breadcrumb and 4 metric cards', () => {
    renderRoomPage();

    expect(screen.getByText('Danh mục Buồng khám & Kỹ thuật')).toBeInTheDocument();
    expect(screen.getByText('TỔNG SỐ BUỒNG KHÁM')).toBeInTheDocument();
    expect(screen.getByText('24 Phòng')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('20 Phòng')).toBeInTheDocument();
    expect(screen.getByText('TẠM NGỪNG / BẢO TRÌ')).toBeInTheDocument();
    expect(screen.getByText('04 Phòng')).toBeInTheDocument();
    expect(screen.getByText('KHOA PHÒNG LIÊN KẾT')).toBeInTheDocument();
    expect(screen.getByText('14 Khoa phòng')).toBeInTheDocument();
  });

  it('renders filter bar inputs and action buttons', () => {
    renderRoomPage();

    expect(screen.getByPlaceholderText(/Tìm theo mã phòng hoặc tên phòng/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Lọc theo khoa phòng/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Lọc theo trạng thái/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Xuất Excel/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thêm buồng khám/i })).toBeInTheDocument();
  });

  it('renders rooms in table and filters by search query', () => {
    renderRoomPage();

    expect(screen.getByText('RM-101')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Nội tiêu hóa 1')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(/Tìm theo mã phòng hoặc tên phòng/i);
    fireEvent.change(searchInput, { target: { value: 'Tim mạch' } });

    expect(screen.getByText('RM-103')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
  });

  it('filters rooms by department dropdown', () => {
    renderRoomPage();

    const deptSelect = screen.getByLabelText(/Lọc theo khoa phòng/i);
    fireEvent.change(deptSelect, { target: { value: 'Khoa Tim mạch' } });

    expect(screen.getByText('RM-103')).toBeInTheDocument();
    expect(screen.getByText('RM-104')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
  });

  it('filters rooms by status dropdown', () => {
    renderRoomPage();

    const statusSelect = screen.getByLabelText(/Lọc theo trạng thái/i);
    fireEvent.change(statusSelect, { target: { value: 'MAINTENANCE' } });

    expect(screen.getByText('RM-104')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
  });

  it('shows empty message when no rooms match filter', () => {
    renderRoomPage();

    const searchInput = screen.getByPlaceholderText(/Tìm theo mã phòng hoặc tên phòng/i);
    fireEvent.change(searchInput, { target: { value: 'XYZ-NOT-FOUND' } });

    expect(
      screen.getByText(/Không tìm thấy buồng khám nào phù hợp với bộ lọc/i),
    ).toBeInTheDocument();
  });
});
