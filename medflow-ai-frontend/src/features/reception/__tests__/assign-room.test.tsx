import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { AssignRoomSelector } from '../components/assign-room-selector';
import { RoomCompatibilityCard } from '../components/room-compatibility-card';
import { ASSIGN_ROOM_SERVICE, AVAILABLE_ROOMS_MOCK } from '../data/assign-room-mock';
import { AssignRoomPage } from '../pages/assign-room-page';

const roomsDto = {
  visitId: 1,
  visitCode: 'VIS-1',
  visitStatus: 'CHECKED_IN',
  hospitalId: 1,
  assignedDoctorId: 7,
  assignedDoctorCode: 'BS-07',
  assignedDoctorName: 'BS.CKI Trần Quốc Tuấn',
  currentExaminationRoomId: null,
  currentExaminationRoomCode: null,
  requiredDepartmentId: 1,
  requiredDepartmentName: 'Nội tổng quát',
  rooms: [
    {
      id: 206,
      roomCode: 'P206',
      name: 'Phòng 206',
      roomType: 'EXAMINATION',
      location: 'Tầng 2',
      hospitalId: 1,
      departmentId: 1,
      departmentCode: 'NOI',
      departmentName: 'Nội tổng quát',
      capacity: 2,
      currentOccupancy: 0,
      availableCapacity: 2,
      status: 'ACTIVE',
      isCompatible: true,
      isAvailable: true,
      unavailableReason: null,
    },
  ],
};

function renderAssignRoomPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/reception/assign-room?visitId=VIS-1']}>
        <AssignRoomPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Assign Room (Issue #79)', () => {
  it('card shows status badge, waiting count and compatibility', () => {
    const onSelect = vi.fn();
    render(
      <RoomCompatibilityCard room={AVAILABLE_ROOMS_MOCK[0]} selected={false} onSelect={onSelect} />,
    );
    expect(screen.getByText('Sẵn sàng')).toBeInTheDocument();
    expect(screen.getByText(/Sức chứa: 2 người\/slot/)).toBeInTheDocument();
    expect(screen.getByText(/Đang chờ: 2/)).toBeInTheDocument();
    expect(screen.getByText(/Tương thích: 100%/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio'));
    expect(onSelect).toHaveBeenCalledWith('room-206');
  });

  it('selector renders all rooms and service context', () => {
    render(
      <AssignRoomSelector
        {...ASSIGN_ROOM_SERVICE}
        rooms={AVAILABLE_ROOMS_MOCK}
        selectedRoomId={null}
        onSelect={vi.fn()}
      />,
    );
    expect(screen.getByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(3);
    expect(screen.getByText('Đang bận')).toBeInTheDocument();
    expect(screen.getByText('Quá tải')).toBeInTheDocument();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('page loads compatible rooms and assigns the selected room via API', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: roomsDto } as never);
    const put = vi.spyOn(apiClient, 'put').mockResolvedValue({
      data: { roomName: 'Phòng 206', queueNumber: 12 },
    } as never);

    renderAssignRoomPage();

    const confirm = screen.getByRole('button', { name: 'Xác nhận gán buồng khám' });
    expect(confirm).toBeDisabled();

    fireEvent.click(await screen.findByRole('radio', { name: /Phòng 206/ }, { timeout: 5000 }));
    expect(screen.getByText(/Buồng đã chọn: Phòng 206/)).toBeInTheDocument();
    expect(confirm).toBeEnabled();

    fireEvent.click(confirm);

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(put.mock.calls[0]?.[0]).toBe('/reception/visits/VIS-1/examination-room');
    expect(await screen.findByText(/Đã gán buồng khám Phòng 206/)).toBeInTheDocument();
  });
});
