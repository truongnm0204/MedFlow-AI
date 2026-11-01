import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { resetMockAppointments } from '../data/appointment-list-mock';
import { AppointmentConfirmationPage } from '../pages/appointment-confirmation-page';
import { AppointmentReviewPage } from '../pages/appointment-review-page';
import { useBookingStore } from '../store/booking.store';

function renderBookingCompletionFlow(initialEntry: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/appointment/review" element={<AppointmentReviewPage />} />
          <Route
            path="/appointment/confirmation/:appointmentId"
            element={<AppointmentConfirmationPage />}
          />
          <Route path="/appointment/doctors" element={<div>Danh bạ bác sĩ</div>} />
          <Route path="/appointment/services" element={<div>Danh mục dịch vụ</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentReviewPage & AppointmentConfirmationPage (SCR-APT-06 / SCR-APT-07)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetMockAppointments();
    useBookingStore.getState().reset();
  });

  it('shows invalid state when query params are missing on /appointment/review', () => {
    renderBookingCompletionFlow('/appointment/review');

    expect(
      screen.getByText('Thông tin đặt khám không đầy đủ hoặc khung giờ đã hết chỗ'),
    ).toBeInTheDocument();
  });

  it('renders SCR-APT-06 review summary and confirms booking to SCR-APT-07 confirmation page', async () => {
    useBookingStore.getState().setSymptomNote('Đau thượng vị');

    renderBookingCompletionFlow(
      '/appointment/review?mode=doctor&doctorId=doc-01&serviceId=srv-th-01&patientId=pat-self&date=2026-10-13&slotId=slot-0900',
    );

    expect(await screen.findByText('BƯỚC 3 — HIỆN TẠI')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText(/09:00 - 09:30/)).toBeInTheDocument();
    expect(screen.getByText('"Đau thượng vị"')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/i }));

    expect(
      await screen.findByRole('heading', { name: 'ĐẶT LỊCH KHÁM NGOẠI TRÚ THÀNH CÔNG!' }),
    ).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-1013-0900')).toBeInTheDocument();
    expect(screen.getByText('STT: 013')).toBeInTheDocument();

    await waitFor(() => {
      expect(useBookingStore.getState().symptomNote).toBe('');
    });
  });

  it('shows invalid state when selected slot is FULL (slot-0730)', async () => {
    renderBookingCompletionFlow(
      '/appointment/review?mode=doctor&doctorId=doc-01&serviceId=srv-th-01&patientId=pat-self&date=2026-10-13&slotId=slot-0730',
    );

    expect(
      await screen.findByText('Thông tin đặt khám không đầy đủ hoặc khung giờ đã hết chỗ'),
    ).toBeInTheDocument();
  });
});
