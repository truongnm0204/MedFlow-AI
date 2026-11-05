import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { RoomEditPage } from '../pages/room-edit-page';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

function renderAt(id: string) {
  return render(
    <MemoryRouter initialEntries={[`/admin/rooms/${id}/edit`]}>
      <Routes>
        <Route path="/admin/rooms/:id/edit" element={<RoomEditPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RoomEditPage (SCR-ADM-ROOM-02)', () => {
  beforeEach(() => mockNavigate.mockClear());

  it('pre-fills form and header from :id', () => {
    renderAt('rm-002');
    expect(screen.getByText('Cấu hình Phòng khám Nội tiết 2')).toBeInTheDocument();
    expect(
      screen.getByText('Khởi tạo cơ sở, khoa, vị trí, sức chứa và trạng thái phòng'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Mã phòng *')).toHaveValue('RM-202');
    expect(screen.getByLabelText('Mã phòng *')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Tên phòng *')).toHaveValue('Phòng khám Nội tiết 2');
    expect(screen.getByLabelText('Vị trí phòng *')).toHaveValue(
      'Nhà A - Tầng 2 - Khu khám ngoại trú',
    );
    expect(screen.getByText('1. THÔNG TIN CHÍNH & PHẠM VI')).toBeInTheDocument();
    expect(screen.getByText('2. CẤU HÌNH VẬN HÀNH')).toBeInTheDocument();
  });

  it('resolves by room code too', () => {
    renderAt('RM-202');
    expect(screen.getByLabelText('Mã phòng *')).toHaveValue('RM-202');
  });

  it('updates live preview as fields change', () => {
    renderAt('rm-002');
    fireEvent.change(screen.getByLabelText('Tên phòng *'), { target: { value: 'Phòng mới' } });
    fireEvent.click(screen.getByRole('radio', { name: '4 người' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Loại phòng PROCEDURAL' }));
    const preview = screen.getByTestId('room-live-preview');
    expect(preview).toHaveTextContent('Phòng mới');
    expect(preview).toHaveTextContent('4 người');
    expect(preview).toHaveTextContent('PROCEDURAL');
  });

  it('shows validation error for empty name and blocks submit', async () => {
    renderAt('rm-002');
    fireEvent.change(screen.getByLabelText('Tên phòng *'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(await screen.findByText('Vui lòng nhập tên phòng')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('submits valid data and navigates to list', async () => {
    renderAt('rm-002');
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms'));
  });

  it('cancel navigates back to list', () => {
    renderAt('rm-002');
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms');
  });

  it('shows not-found state for unknown id', () => {
    renderAt('nope');
    expect(screen.getByText('Không tìm thấy buồng khám')).toBeInTheDocument();
  });
});
