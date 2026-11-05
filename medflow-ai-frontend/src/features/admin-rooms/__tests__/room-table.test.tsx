import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { RoomTable } from '../components/room-table';
import type { RoomItem } from '../types/room.types';

const testRooms: RoomItem[] = [
  {
    id: 'rm-001',
    roomCode: 'RM-101',
    roomName: 'Phòng khám Nội tiêu hóa 1',
    department: 'Khoa Nội Tiêu hóa',
    location: 'Tầng 1 - Khu A',
    facility: 'Cơ sở 1 - Hà Nội',
    status: 'ACTIVE',
    capacityPerSlot: 2,
  },
  {
    id: 'rm-010',
    roomCode: 'RM-302',
    roomName: 'Phòng khám Da liễu 2',
    department: 'Khoa Da liễu',
    location: 'Tầng 3 - Khu A',
    facility: 'Cơ sở 1 - Hà Nội',
    status: 'INACTIVE',
    capacityPerSlot: 2,
  },
];

describe('RoomTable component', () => {
  it('renders room columns and badges', () => {
    render(<RoomTable rooms={testRooms} />);

    expect(screen.getByText('RM-101')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Nội tiêu hóa 1')).toBeInTheDocument();
    expect(screen.getByText('Đang hoạt động')).toBeInTheDocument();

    expect(screen.getByText('RM-302')).toBeInTheDocument();
    expect(screen.getByText('Phòng khám Da liễu 2')).toBeInTheDocument();
    expect(screen.getByText('Tạm ngừng')).toBeInTheDocument();
  });

  it('triggers onRequestDeactivate when "Ngừng hoạt động" is clicked on active room', () => {
    const handleDeactivate = vi.fn();
    render(<RoomTable rooms={testRooms} onRequestDeactivate={handleDeactivate} />);

    const deactivateBtn = screen.getByRole('button', {
      name: /Ngừng hoạt động buồng khám Phòng khám Nội tiêu hóa 1/i,
    });
    fireEvent.click(deactivateBtn);

    expect(handleDeactivate).toHaveBeenCalledWith(testRooms[0]);
  });

  it('triggers onToggleStatus when "Kích hoạt lại" is clicked on inactive room', () => {
    const handleToggle = vi.fn();
    render(<RoomTable rooms={testRooms} onToggleStatus={handleToggle} />);

    const reactivateBtn = screen.getByRole('button', {
      name: /Kích hoạt lại buồng khám Phòng khám Da liễu 2/i,
    });
    fireEvent.click(reactivateBtn);

    expect(handleToggle).toHaveBeenCalledWith('rm-010');
  });

  it('triggers onViewRoom when "Xem" is clicked', () => {
    const handleView = vi.fn();
    render(<RoomTable rooms={testRooms} onViewRoom={handleView} />);

    const viewBtn = screen.getByRole('button', {
      name: /Xem buồng khám Phòng khám Nội tiêu hóa 1/i,
    });
    fireEvent.click(viewBtn);

    expect(handleView).toHaveBeenCalledWith(testRooms[0]);
  });
});
