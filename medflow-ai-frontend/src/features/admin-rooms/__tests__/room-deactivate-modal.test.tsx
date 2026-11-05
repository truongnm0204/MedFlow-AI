import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { RoomDeactivateModal } from '../components/room-deactivate-modal';
import type { RoomItem } from '../types/room.types';

const unblockedRoom: RoomItem = {
  id: 'rm-001',
  roomCode: 'RM-101',
  roomName: 'Phòng khám Nội tiêu hóa 1',
  department: 'Khoa Nội Tiêu hóa',
  location: 'Tầng 1 - Khu A',
  facility: 'Cơ sở 1 - Hà Nội',
  status: 'ACTIVE',
  capacityPerSlot: 2,
  hasActiveDependencies: false,
  assignedDoctorsCount: 0,
  activeVisitsCount: 0,
  activeSlotsCount: 0,
};

const blockedRoom: RoomItem = {
  id: 'rm-002',
  roomCode: 'RM-102',
  roomName: 'Phòng khám Nội tiêu hóa 2',
  department: 'Khoa Nội Tiêu hóa',
  location: 'Tầng 1 - Khu A',
  facility: 'Cơ sở 1 - Hà Nội',
  status: 'ACTIVE',
  capacityPerSlot: 2,
  hasActiveDependencies: true,
  assignedDoctorsCount: 2,
  activeVisitsCount: 4,
  activeSlotsCount: 1,
};

describe('RoomDeactivateModal (SCR-ADM-ROOM-05)', () => {
  it('does not render when open is false or room is null', () => {
    const { rerender } = render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={false}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    expect(screen.queryByText(/Ngừng hoạt động buồng khám/i)).not.toBeInTheDocument();

    rerender(<RoomDeactivateModal room={null} open={true} onClose={vi.fn()} onConfirm={vi.fn()} />);
    expect(screen.queryByText(/Ngừng hoạt động buồng khám/i)).not.toBeInTheDocument();
  });

  it('renders room details properly when opened', () => {
    render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={true}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(
      screen.getByRole('heading', { name: /Ngừng hoạt động buồng khám/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Phòng khám Nội tiêu hóa 1/)).toBeInTheDocument();
    expect(screen.getByText('RM-101')).toBeInTheDocument();
  });

  it('displays blocked warning and disables confirmation when room has active dependencies', () => {
    const handleConfirm = vi.fn();
    render(
      <RoomDeactivateModal
        room={blockedRoom}
        open={true}
        onClose={vi.fn()}
        onConfirm={handleConfirm}
      />,
    );

    expect(
      screen.getByText(
        /Không thể ngừng hoạt động khi còn.*2 bác sĩ đang trực.*4 lượt khám đang xử lý/i,
      ),
    ).toBeInTheDocument();

    const confirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmBtn).toBeDisabled();

    fireEvent.click(confirmBtn);
    expect(handleConfirm).not.toHaveBeenCalled();
  });

  it('allows confirming when room has 0 dependencies', async () => {
    const handleConfirm = vi.fn();
    render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={true}
        onClose={vi.fn()}
        onConfirm={handleConfirm}
      />,
    );

    expect(
      screen.getByText(
        /Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc ca khám chưa được giải quyết/i,
      ),
    ).toBeInTheDocument();

    const confirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmBtn).not.toBeDisabled();

    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(handleConfirm).toHaveBeenCalledWith('rm-001');
    });
  });

  it('calls onClose when "Giữ buồng khám" button is clicked', () => {
    const handleClose = vi.fn();
    render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={true}
        onClose={handleClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Giữ buồng khám' }));
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape key is pressed', () => {
    const handleClose = vi.fn();
    render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={true}
        onClose={handleClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('renders loading state when isSubmitting is true', () => {
    render(
      <RoomDeactivateModal
        room={unblockedRoom}
        open={true}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
        isSubmitting={true}
      />,
    );

    expect(screen.getByRole('button', { name: 'Đang xử lý...' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Giữ buồng khám' })).toBeDisabled();
  });
});
