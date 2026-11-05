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

  it('completes deactivation flow for an unblocked room (M2-179)', async () => {
    renderRoomPage();

    expect(screen.getByText('20 Phòng')).toBeInTheDocument();
    expect(screen.getByText('04 Phòng')).toBeInTheDocument();

    // Find button to deactivate RM-101
    const deactivateBtn = screen.getByRole('button', {
      name: /Ngừng hoạt động buồng khám Phòng khám Nội tiêu hóa 1/i,
    });
    fireEvent.click(deactivateBtn);
    // Modal opens
    expect(
      screen.getByRole('heading', { name: /Ngừng hoạt động buồng khám/i }),
    ).toBeInTheDocument();

    // Confirm
    const confirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    fireEvent.click(confirmBtn);

    // Success banner appears
    expect(
      await screen.findByText(/Đã chuyển trạng thái buồng khám.*sang Ngừng hoạt động/i),
    ).toBeInTheDocument();

    // Metrics updated
    expect(screen.getByText('19 Phòng')).toBeInTheDocument();
    expect(screen.getByText('05 Phòng')).toBeInTheDocument();
  });

  it('blocks deactivation when room has active dependencies (M2-179)', () => {
    renderRoomPage();

    // Find button to deactivate RM-102 (has 1 doctor, 3 visits, 2 slots)
    const deactivateBtn = screen.getByRole('button', {
      name: /Ngừng hoạt động buồng khám Phòng khám Nội tiêu hóa 2/i,
    });
    fireEvent.click(deactivateBtn);

    // Modal opens with blocked alert
    expect(
      screen.getByRole('heading', { name: /Ngừng hoạt động buồng khám/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Không thể ngừng hoạt động khi còn.*bác sĩ đang trực/i),
    ).toBeInTheDocument();

    const confirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmBtn).toBeDisabled();
  });

  it('completes reactivation flow for an inactive room (M2-179)', async () => {
    renderRoomPage();

    // Filter to INACTIVE rooms
    const statusSelect = screen.getByLabelText(/Lọc theo trạng thái/i);
    fireEvent.change(statusSelect, { target: { value: 'INACTIVE' } });

    expect(screen.getByText('RM-302')).toBeInTheDocument();

    const reactivateBtn = screen.getByRole('button', {
      name: /Kích hoạt lại buồng khám Phòng khám Da liễu 2/i,
    });
    fireEvent.click(reactivateBtn);

    expect(
      await screen.findByText(/Đã kích hoạt lại hoạt động cho buồng khám/i),
    ).toBeInTheDocument();
  });
});
