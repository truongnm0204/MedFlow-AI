import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { AppointmentSlotBookingPage } from '../pages/appointment-slot-booking-page';
import { useBookingStore } from '../store/booking.store';

function renderSlotBookingPage(initialEntry = '/appointment/doctors/doc-01/booking') {
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
            path="/appointment/doctors/:doctorId/booking"
            element={<AppointmentSlotBookingPage />}
          />
          <Route path="/appointment/doctors" element={<div>Danh bạ Bác sĩ Mock</div>} />
          <Route path="/appointment/review" element={<div>Soát xét Đặt lịch Mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentSlotBookingPage — Đặt khám theo Bác sĩ (SCR-APT-05)', () => {
  beforeEach(() => {
    useBookingStore.getState().reset();
  });

  it('renders stepper, doctor summary banner, patient profiles, date strip, and doctor time slots', () => {
    renderSlotBookingPage();

    expect(screen.getByText('BƯỚC 2 — HIỆN TẠI')).toBeInTheDocument();
    expect(screen.getByText('Ngày & Giờ khám')).toBeInTheDocument();

    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText('(Mã: DV-TH-01)')).toBeInTheDocument();

    expect(screen.getByText('1. Chọn Người Thăm Khám')).toBeInTheDocument();
    expect(screen.getAllByText('Nguyễn Văn An').length).toBeGreaterThan(0);
    expect(screen.getByText('Nguyễn Bảo Châu')).toBeInTheDocument();
    expect(screen.getByText('Lê Thị Mai')).toBeInTheDocument();

    expect(screen.getByText('2. Chọn Ngày Khám')).toBeInTheDocument();
    expect(screen.getByText('Tháng 10, 2026')).toBeInTheDocument();

    expect(screen.getByText('3. Chọn Khung Giờ Thăm Khám')).toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI SÁNG (07:30 - 11:30)')).toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI CHIỀU (13:30 - 16:30)')).toBeInTheDocument();
    expect(screen.getByText('Còn 1 slot cuối')).toBeInTheDocument();
  });

  it('displays only the doctor shift slots when selecting a date where the doctor works only one shift', async () => {
    renderSlotBookingPage();

    // Switch date to Thứ 4 (14/10) where doctor only works Afternoon shift (Morning is endoscopy surgery)
    const wednesdayOption = screen.getByRole('option', { name: /Thứ 4/i });
    fireEvent.click(wednesdayOption);
    expect(wednesdayOption).toHaveAttribute('aria-selected', 'true');

    expect(
      await screen.findByText(/Buổi sáng bác sĩ có lịch mổ nội soi tiêu hóa tại khoa/),
    ).toBeInTheDocument();
    expect(screen.queryByText('CA KHÁM BUỔI SÁNG (07:30 - 11:30)')).not.toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI CHIỀU (13:30 - 16:30)')).toBeInTheDocument();

    // Select afternoon slot 16:00 - 16:30
    const afternoonLastSlot = screen.getByRole('button', { name: /16:00 - 16:30/i });
    fireEvent.click(afternoonLastSlot);

    expect(
      screen.getByText(/16:00 - 16:30 • Thứ Tư, 14\/10\/2026 \(Ca Chiều\)/),
    ).toBeInTheDocument();
  });

  it('prevents selecting full or off-duty dates and full time slots', () => {
    renderSlotBookingPage();

    const fullDateOption = screen.getByRole('option', { name: /Thứ 2/i });
    const sundayOffOption = screen.getByRole('option', { name: /Chủ Nhật/i });
    expect(fullDateOption).toBeDisabled();
    expect(sundayOffOption).toBeDisabled();

    const fullMorningSlot = screen.getByRole('button', { name: /07:30 - 08:00/i });
    expect(fullMorningSlot).toBeDisabled();
  });

  it('records symptom notes and navigates to review step on continue', () => {
    renderSlotBookingPage();

    const textarea = screen.getByLabelText('Lý do khám / Triệu chứng ban đầu');
    fireEvent.change(textarea, {
      target: { value: 'Đau thượng vị âm ỉ 3 ngày nay' },
    });
    expect(useBookingStore.getState().symptomNote).toBe('Đau thượng vị âm ỉ 3 ngày nay');

    const continueButton = screen.getByRole('button', {
      name: /Tiếp tục: Soát xét Đặt lịch/i,
    });
    fireEvent.click(continueButton);

    expect(screen.getByText('Soát xét Đặt lịch Mock')).toBeInTheDocument();
  });
});
