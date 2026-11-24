import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { RoomEditPage } from '../pages/room-edit-page';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

function renderAt(id: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/admin/rooms/${id}/edit`]}>
        <Routes>
          <Route path="/admin/rooms/:id/edit" element={<RoomEditPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RoomEditPage (SCR-ADM-ROOM-02 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (String(url).includes('/admin/rooms')) {
        return {
          data: {
            data: [
              {
                id: 2,
                roomCode: 'RM-202',
                name: 'Phòng khám Nội tiết 2',
                departmentName: 'Khoa Nội tiết',
                roomType: 'OUTPATIENT',
                location: 'Nhà A - Tầng 2 - Khu khám ngoại trú',
                capacity: 2,
                status: 'ACTIVE',
                isActive: true,
              },
            ],
          },
        };
      }
      return { data: { data: [] } };
    });
    vi.mocked(apiClient.put).mockResolvedValue({
      data: {
        id: 2,
        roomCode: 'RM-202',
        name: 'Phòng khám Nội tiết 2',
        departmentName: 'Khoa Nội tiết',
        roomType: 'OUTPATIENT',
        location: 'Nhà A - Tầng 2 - Khu khám ngoại trú',
        capacity: 2,
        status: 'ACTIVE',
        isActive: true,
      },
    });
  });

  it('pre-fills form from API by numeric id or roomCode and updates live preview', async () => {
    const first = renderAt('2');
    expect(await screen.findByText('Cấu hình Phòng khám Nội tiết 2')).toBeInTheDocument();
    expect(screen.getByLabelText('Mã phòng *')).toHaveValue('RM-202');
    expect(screen.getByLabelText('Mã phòng *')).toHaveAttribute('readonly');

    fireEvent.change(screen.getByLabelText('Tên phòng *'), { target: { value: 'Phòng mới' } });
    fireEvent.click(screen.getByRole('radio', { name: '4 người' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Loại phòng PROCEDURAL' }));
    const preview = screen.getByTestId('room-live-preview');
    expect(preview).toHaveTextContent('Phòng mới');
    expect(preview).toHaveTextContent('4 người');
    expect(preview).toHaveTextContent('PROCEDURAL');
    first.unmount();

    renderAt('RM-202');
    expect(await screen.findByDisplayValue('RM-202')).toBeInTheDocument();
  });

  it('validates empty name, submits PUT /admin/rooms/:id, and shows alert for unknown id', async () => {
    const first = renderAt('2');
    expect(await screen.findByDisplayValue('Phòng khám Nội tiết 2')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Tên phòng *'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(await screen.findByText('Vui lòng nhập tên phòng')).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Tên phòng *'), {
      target: { value: 'Phòng khám Nội tiết 2 Cập nhật' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/rooms/2',
        expect.objectContaining({ name: 'Phòng khám Nội tiết 2 Cập nhật' }),
      );
      expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms');
    });
    first.unmount();

    renderAt('nope');
    expect(await screen.findByText('Không tìm thấy buồng khám')).toBeInTheDocument();
  });
});
