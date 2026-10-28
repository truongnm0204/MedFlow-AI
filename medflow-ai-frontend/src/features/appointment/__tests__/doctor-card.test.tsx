import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DoctorCard } from '../components/doctor-card';
import { MOCK_DOCTORS } from '../data/doctor-search-mock';
import { useBookingStore } from '../store/booking.store';

const mockDoctor = MOCK_DOCTORS[0]; // BS.CKII Trần Quốc Tuấn

describe('DoctorCard', () => {
  beforeEach(() => {
    useBookingStore.getState().reset();
  });

  it('renders doctor information correctly', () => {
    render(
      <MemoryRouter>
        <DoctorCard doctor={mockDoctor} />
      </MemoryRouter>,
    );

    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('Bác sĩ Trưởng kíp Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText(/Khoa Tiêu Hóa - Gan Mật • 15 năm kinh nghiệm/)).toBeInTheDocument();
    expect(screen.getByText('Phòng 204 - Tầng 2, Dãy B')).toBeInTheDocument();
    expect(screen.getByText('Hôm nay: 14:00 - 16:30')).toBeInTheDocument();
    expect(screen.getByText('Đang nhận lịch')).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();
  });

  it('calls selectDoctor and triggers booking action when clicking Đặt khám ngay', () => {
    render(
      <MemoryRouter>
        <DoctorCard doctor={mockDoctor} />
      </MemoryRouter>,
    );

    const bookButton = screen.getByRole('button', { name: 'Đặt khám ngay' });
    fireEvent.click(bookButton);

    expect(useBookingStore.getState().selectedDoctorId).toBe(mockDoctor.id);
  });

  it('triggers onViewProfile callback when provided', () => {
    const handleViewProfile = vi.fn();
    render(
      <MemoryRouter>
        <DoctorCard doctor={mockDoctor} onViewProfile={handleViewProfile} />
      </MemoryRouter>,
    );

    const viewButton = screen.getByRole('button', { name: 'Xem hồ sơ' });
    fireEvent.click(viewButton);

    expect(handleViewProfile).toHaveBeenCalledWith(mockDoctor);
  });
});
