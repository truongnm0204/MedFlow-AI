import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { AssignRoomSelector } from '../components/assign-room-selector';
import { RoomCompatibilityCard } from '../components/room-compatibility-card';
import { ASSIGN_ROOM_SERVICE, AVAILABLE_ROOMS_MOCK } from '../data/assign-room-mock';
import { AssignRoomPage } from '../pages/assign-room-page';

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

  it('page disables confirm until a room is selected, then confirms', () => {
    render(
      <MemoryRouter>
        <AssignRoomPage />
      </MemoryRouter>,
    );
    const confirm = screen.getByRole('button', { name: 'Xác nhận gán buồng khám' });
    expect(confirm).toBeDisabled();
    fireEvent.click(screen.getByRole('radio', { name: /Phòng 206/ }));
    expect(screen.getByText(/Buồng đã chọn: Phòng 206/)).toBeInTheDocument();
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(screen.getByText(/Đã gán buồng khám: Phòng 206/)).toBeInTheDocument();
  });
});
