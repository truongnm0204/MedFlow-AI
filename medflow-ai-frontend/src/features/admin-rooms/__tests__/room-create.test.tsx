import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { RoomCreatePage } from '../pages/room-create-page';
import { roomCreateSchema } from '../schemas/room-create.schema';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <RoomCreatePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RoomCreatePage (SCR-ADM-ROOM-02 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: [
          {
            id: 12,
            code: 'DEPT-ENDO',
            name: 'Khoa Nội tiết',
            facility: 'MedFlow Cơ sở 1',
            isActive: true,
            status: 'ACTIVE',
          },
        ],
      },
    });
    vi.mocked(apiClient.post).mockResolvedValue({
      data: {
        id: 22,
        roomCode: 'RM-202',
        name: 'Phòng khám Nội tiết 2',
        departmentName: 'Khoa Nội tiết',
        roomType: 'OUTPATIENT',
        location: 'Nhà A - Tầng 2',
        capacity: 1,
        status: 'ACTIVE',
        isActive: true,
      },
    });
  });

  it('renders title and updates live preview while typing', () => {
    renderPage();
    expect(
      screen.getByRole('heading', { level: 1, name: 'Khai báo buồng khám' }),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Mã phòng *'), { target: { value: 'RM-202' } });
    fireEvent.change(screen.getByLabelText('Khoa phụ trách *'), {
      target: { value: 'Khoa Nhi' },
    });
    fireEvent.click(screen.getByRole('button', { name: '3 người / slot' }));
    expect(screen.getByTestId('preview-Mã phòng')).toHaveTextContent('RM-202');
    expect(screen.getByTestId('preview-Khoa phụ trách')).toHaveTextContent('Khoa Nhi');
    expect(screen.getByTestId('preview-Sức chứa')).toHaveTextContent('3 người / slot');
  });

  it('shows validation errors and does not submit on empty fields', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo buồng khám' }));
    expect(await screen.findByText('Vui lòng nhập mã phòng')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng chọn khoa phụ trách')).toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('submits POST /admin/rooms with resolved departmentId and navigates to list', async () => {
    renderPage();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalled());

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

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/rooms',
        expect.objectContaining({
          roomCode: 'RM-202',
          name: 'Phòng khám Nội tiết 2',
          departmentId: 12,
          location: 'Nhà A - Tầng 2',
        }),
      );
      expect(mockNavigate).toHaveBeenCalledWith('/admin/rooms');
    });
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
  it('accepts valid input and rejects bad code/capacity', () => {
    expect(roomCreateSchema.safeParse(valid).success).toBe(true);
    expect(roomCreateSchema.safeParse({ ...valid, code: 'X1' }).success).toBe(false);
    expect(roomCreateSchema.safeParse({ ...valid, capacity: 5 }).success).toBe(false);
  });
});
