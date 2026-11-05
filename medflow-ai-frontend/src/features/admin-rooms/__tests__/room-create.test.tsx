import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { roomCreateSchema } from '../schemas/room-create.schema';
import { RoomCreatePage } from '../pages/room-create-page';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

function renderPage() {
  return render(
    <MemoryRouter>
      <RoomCreatePage />
    </MemoryRouter>,
  );
}

describe('RoomCreatePage (SCR-ADM-ROOM-02)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
  });

  it('renders title and form sections', () => {
    renderPage();
    expect(
      screen.getByRole('heading', { level: 1, name: 'Khai báo buồng khám' }),
    ).toBeInTheDocument();
    expect(screen.getByText('1. THÔNG TIN CHÍNH & PHẠM VI')).toBeInTheDocument();
    expect(screen.getByText('2. CẤU HÌNH VẬN HÀNH')).toBeInTheDocument();
  });

  it('updates live preview while typing', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Mã phòng *'), { target: { value: 'RM-202' } });
    fireEvent.change(screen.getByLabelText('Khoa phụ trách *'), {
      target: { value: 'Khoa Nhi' },
    });
    fireEvent.click(screen.getByRole('button', { name: '3 người / slot' }));
    expect(screen.getByTestId('preview-Mã phòng')).toHaveTextContent('RM-202');
    expect(screen.getByTestId('preview-Khoa phụ trách')).toHaveTextContent('Khoa Nhi');
    expect(screen.getByTestId('preview-Sức chứa')).toHaveTextContent('3 người / slot');
  });

  it('shows validation errors and does not navigate on empty submit', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo buồng khám' }));
    expect(await screen.findByText('Vui lòng nhập mã phòng')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng chọn khoa phụ trách')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('submits valid data and navigates to list', async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Mã phòng *'), { target: { value: 'RM-202' } });
    fireEvent.change(screen.getByLabelText('Tên phòng *'), {
      target: { value: 'Phòng khám Nội tiết 2' },
    });
    fireEvent.change(screen.getByLabelText('Khoa phụ trách *'), {
      target: { value: 'Khoa Nội tiết' },
    });
    fireEvent.change(screen.getByLabelText('Vị trí phòng *'), {
      target: { value: 'Nhà A - Tầng 2' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo buồng khám' }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms'));
  });

  it('saves draft and cancels', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Lưu bản nháp' }));
    expect(screen.getByText('Đã lưu bản nháp buồng khám.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms');
  });
});

describe('roomCreateSchema', () => {
  const valid = {
    code: 'RM-202',
    name: 'Phòng A',
    type: 'OUTPATIENT',
    department: 'Khoa Nhi',
    location: 'Nhà A',
    capacity: 2,
    status: 'ACTIVE',
  };
  it('accepts valid input', () => {
    expect(roomCreateSchema.safeParse(valid).success).toBe(true);
  });
  it('rejects bad code and capacity', () => {
    expect(roomCreateSchema.safeParse({ ...valid, code: 'X1' }).success).toBe(false);
    expect(roomCreateSchema.safeParse({ ...valid, capacity: 5 }).success).toBe(false);
  });
});
