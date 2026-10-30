import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { ServiceSlotBookingPage } from '../pages/service-slot-booking-page';
import { useBookingStore } from '../store/booking.store';

function renderServiceBookingPage(initialEntry = '/appointment/services/srv-th-01/booking') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route
            path="/appointment/services/:serviceId/booking"
            element={<ServiceSlotBookingPage />}
          />
          <Route path="/appointment/services" element={<div>Danh mục Dịch vụ Mock</div>} />
          <Route path="/appointment/review" element={<div>Soát xét Đặt lịch Dịch vụ Mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ServiceSlotBookingPage — Đặt khám theo Dịch vụ (Chọn Ngày & Ca khám)', () => {
  beforeEach(() => {
    useBookingStore.getState().reset();
  });

  it('renders service-only banner without doctor info and shows date + shift selection', () => {
    renderServiceBookingPage();

    expect(screen.getAllByText('Dịch vụ khám').length).toBeGreaterThan(0);
    expect(screen.getByText('Ngày & Ca khám')).toBeInTheDocument();

    expect(screen.getAllByText('Khám Chuyên khoa Tiêu Hóa').length).toBeGreaterThan(0);
    expect(screen.getByText('DV-TH-01')).toBeInTheDocument();
    expect(screen.getByText('Đổi dịch vụ khám')).toBeInTheDocument();

    // Must NOT render a doctor profile card
    expect(screen.queryByText('BS.CKII Trần Quốc Tuấn')).not.toBeInTheDocument();

    // Renders Date & Shift selection (not 30-min doctor slots)
    expect(screen.getByText('2. Chọn Ngày Khám')).toBeInTheDocument();
    expect(screen.getByText('3. Chọn Ca Khám Dịch Vụ')).toBeInTheDocument();
    expect(screen.getByText('Ca Khám Buổi Sáng')).toBeInTheDocument();
    expect(screen.getByText('Ca Khám Buổi Chiều')).toBeInTheDocument();
  });

  it('allows selecting afternoon shift and updates service sticky summary footer', () => {
    renderServiceBookingPage();

    const afternoonShiftRadio = screen.getByRole('radio', { name: /Ca Khám Buổi Chiều/i });
    fireEvent.click(afternoonShiftRadio);
    expect(afternoonShiftRadio).toHaveAttribute('aria-checked', 'true');

    expect(
      screen.getByText(/Ca Khám Buổi Chiều \(13:30 - 16:30\) • Thứ Ba, 13\/10\/2026/),
    ).toBeInTheDocument();

    const continueBtn = screen.getByRole('button', {
      name: /Tiếp tục: Soát xét Đặt lịch/i,
    });
    fireEvent.click(continueBtn);

    expect(screen.getByText('Soát xét Đặt lịch Dịch vụ Mock')).toBeInTheDocument();
  });
});
