import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';
import { RoomListPage } from '../pages/room-list-page';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
  },
}));

const INITIAL_API_ROOMS = [
  {
    id: 1,
    roomCode: 'RM-101',
    name: 'Phòng khám Nội tiêu hóa 1',
    departmentName: 'Khoa Nội Tiêu hóa',
    location: 'Nhà A - Tầng 1',
    facility: 'MedFlow Cơ sở 1',
    roomType: 'OUTPATIENT',
    capacity: 2,
    status: 'ACTIVE',
    isActive: true,
    hasActiveDependencies: false,
    assignedDoctorsCount: 0,
    activeVisitsCount: 0,
    activeSlotsCount: 0,
  },
  {
    id: 2,
    roomCode: 'RM-102',
    name: 'Phòng khám Nội tiêu hóa 2',
    departmentName: 'Khoa Nội Tiêu hóa',
    location: 'Nhà A - Tầng 1',
    facility: 'MedFlow Cơ sở 1',
    roomType: 'OUTPATIENT',
    capacity: 2,
    status: 'ACTIVE',
    isActive: true,
    hasActiveDependencies: true,
    assignedDoctorsCount: 1,
    activeVisitsCount: 3,
    activeSlotsCount: 2,
  },
  {
    id: 3,
    roomCode: 'RM-103',
    name: 'Phòng khám Tim mạch 1',
    departmentName: 'Khoa Tim mạch',
    location: 'Nhà B - Tầng 2',
    facility: 'MedFlow Cơ sở 1',
    roomType: 'OUTPATIENT',
    capacity: 1,
    status: 'ACTIVE',
    isActive: true,
    hasActiveDependencies: false,
  },
  {
    id: 4,
    roomCode: 'RM-104',
    name: 'Phòng Siêu âm Tim mạch',
    departmentName: 'Khoa Tim mạch',
    location: 'Nhà B - Tầng 2',
    facility: 'MedFlow Cơ sở 1',
    roomType: 'PROCEDURAL',
    capacity: 1,
    status: 'MAINTENANCE',
    isActive: true,
    hasActiveDependencies: false,
  },
  {
    id: 5,
    roomCode: 'RM-302',
    name: 'Phòng khám Da liễu 2',
    departmentName: 'Khoa Da liễu',
    location: 'Nhà C - Tầng 3',
    facility: 'MedFlow Cơ sở 1',
    roomType: 'OUTPATIENT',
    capacity: 2,
    status: 'INACTIVE',
    isActive: false,
    hasActiveDependencies: false,
  },
];

describe('RoomListPage (SCR-ADM-ROOM-01 real API)', () => {
  let roomsDb: typeof INITIAL_API_ROOMS;

  beforeEach(() => {
    vi.clearAllMocks();
    roomsDb = JSON.parse(JSON.stringify(INITIAL_API_ROOMS));

    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (String(url).includes('/admin/rooms')) {
        const active = roomsDb.filter((r) => r.status === 'ACTIVE').length;
        const inactive = roomsDb.length - active;
        const departments = new Set(roomsDb.map((r) => r.departmentName)).size;
        return {
          data: {
            data: roomsDb,
            metrics: {
              total: roomsDb.length,
              active,
              inactive,
              departments,
            },
          },
        };
      }
      return { data: [] };
    });

    vi.mocked(apiClient.put).mockImplementation(async (url, body) => {
      const id = Number(String(url).split('/').pop());
      const payload = body as { status: string; isActive: boolean };
      const idx = roomsDb.findIndex((r) => r.id === id);
      if (idx >= 0) {
        roomsDb[idx] = {
          ...roomsDb[idx],
          status: payload.status,
          isActive: payload.isActive,
        };
      }
      return { data: roomsDb[idx] };
    });
  });

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

  it('fetches GET /admin/rooms and renders header, metrics, and table rows', async () => {
    renderRoomPage();

    expect(await screen.findByText('RM-101')).toBeInTheDocument();
    expect(screen.getByText('Danh mục Buồng khám & Kỹ thuật')).toBeInTheDocument();
    expect(screen.getByText('5 Phòng')).toBeInTheDocument();
    expect(screen.getByText('3 Phòng')).toBeInTheDocument();
    expect(screen.getByText('02 Phòng')).toBeInTheDocument();
    expect(screen.getByText('3 Khoa phòng')).toBeInTheDocument();
  });

  it('filters rooms by search query, department, and status', async () => {
    renderRoomPage();
    expect(await screen.findByText('RM-101')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(/Tìm theo mã phòng hoặc tên phòng/i);
    fireEvent.change(searchInput, { target: { value: 'Tim mạch' } });
    expect(screen.getByText('RM-103')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: '' } });
    const statusSelect = screen.getByLabelText(/Lọc theo trạng thái/i);
    fireEvent.change(statusSelect, { target: { value: 'MAINTENANCE' } });
    expect(screen.getByText('RM-104')).toBeInTheDocument();
    expect(screen.queryByText('RM-101')).not.toBeInTheDocument();
  });

  it('calls PUT /admin/rooms/:id on deactivation and reactivation and blocks active dependencies', async () => {
    renderRoomPage();
    expect(await screen.findByText('RM-101')).toBeInTheDocument();

    // Blocked room RM-102
    fireEvent.click(
      screen.getByRole('button', {
        name: /Ngừng hoạt động buồng khám Phòng khám Nội tiêu hóa 2/i,
      }),
    );
    expect(
      screen.getByText(/Không thể ngừng hoạt động khi còn.*bác sĩ đang trực/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /Hủy|Giữ/i }));

    // Unblocked room RM-101
    fireEvent.click(
      screen.getByRole('button', {
        name: /Ngừng hoạt động buồng khám Phòng khám Nội tiêu hóa 1/i,
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/rooms/1',
        expect.objectContaining({ status: 'INACTIVE', isActive: false }),
      );
    });
    expect(
      await screen.findByText(/Đã chuyển trạng thái buồng khám.*sang Ngừng hoạt động/i),
    ).toBeInTheDocument();

    // Reactivate RM-302
    fireEvent.click(
      screen.getByRole('button', {
        name: /Kích hoạt lại buồng khám Phòng khám Da liễu 2/i,
      }),
    );
    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/rooms/5',
        expect.objectContaining({ status: 'ACTIVE', isActive: true }),
      );
    });
  });
});
