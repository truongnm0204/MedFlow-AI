import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { resetMockAppointments } from '../data/appointment-list-mock';
import { AppointmentQueueTrackingPage } from '../pages/appointment-queue-tracking-page';
import { useBookingStore } from '../store/booking.store';

function renderQueueTrackingPage(initialEntry: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route
            path="/appointments/:appointmentId/queue"
            element={<AppointmentQueueTrackingPage />}
          />
          <Route path="/appointments" element={<div>Danh sách lịch hẹn</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentQueueTrackingPage (SCR-QUE-01)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetMockAppointments();
    useBookingStore.getState().reset();
  });

  it('renders live queue tracking status and call board for confirmed appointment apt-1013-0902', async () => {
    renderQueueTrackingPage('/appointments/apt-1013-0902/queue');

    expect(await screen.findByText('STT CỦA BẠN: 012')).toBeInTheDocument();
    expect(screen.getByText('QUEUE: CHỜ CHECK-IN')).toBeInTheDocument();
    expect(screen.getByText('STT: 010')).toBeInTheDocument();
    expect(screen.getByText('STT: 011')).toBeInTheDocument();
  });

  it('renders error alert when appointment is cancelled (apt-0610-1400)', async () => {
    renderQueueTrackingPage('/appointments/apt-0610-1400/queue');

    expect(
      await screen.findByText('Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.'),
    ).toBeInTheDocument();
  });
});
