import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_APPOINTMENTS, resetMockAppointments } from '../data/appointment-list-mock';
import { buildMockQueueStatus } from '../data/appointment-queue-mock';
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
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      const match = String(url).match(/^\/appointments\/([^/]+)\/queue-status$/);
      const id = match ? decodeURIComponent(match[1]!) : '';
      const apt = MOCK_APPOINTMENTS.find((item) => item.id === id || item.appointmentCode === id);
      if (!apt) {
        throw new AxiosError(
          'Not Found',
          'ERR_BAD_REQUEST',
          undefined,
          {},
          {
            status: 404,
            statusText: 'Not Found',
            headers: {},
            config: {} as never,
            data: { status: 404, detail: 'Không tìm thấy lịch hẹn.' },
          },
        );
      }
      if (apt.status === 'CANCELLED') {
        throw new AxiosError(
          'Conflict',
          'ERR_BAD_REQUEST',
          undefined,
          {},
          {
            status: 409,
            statusText: 'Conflict',
            headers: {},
            config: {} as never,
            data: {
              status: 409,
              detail: 'Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.',
            },
          },
        );
      }
      return { data: buildMockQueueStatus(apt) };
    });
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
