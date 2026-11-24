import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { getHospitalTodayIso } from '@/shared/lib/hospital-date';
import { ToastViewport, toast } from '@/shared/ui/toast';

import { getMockAvailableSlotsPayload } from '../data/slot-booking-mock';
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
        <ToastViewport />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentSlotBookingPage — Đặt khám theo Bác sĩ (SCR-APT-05)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useBookingStore.getState().reset();
    toast.clear();

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/appointments/available-slots') {
        const params = (
          config as {
            params?: { doctorId?: string | null; serviceId?: string | null; dateIso?: string };
          }
        )?.params;
        const requestedDate =
          params?.dateIso === getHospitalTodayIso() ? '2026-10-13' : params?.dateIso;
        return {
          data: getMockAvailableSlotsPayload(params?.doctorId, params?.serviceId, requestedDate),
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders stepper, doctor summary banner, patient profiles, date strip, and doctor time slots', async () => {
    renderSlotBookingPage();

    expect(screen.getByText('BƯỚC 2 — HIỆN TẠI')).toBeInTheDocument();
    expect(screen.getByText('Ngày & Giờ khám')).toBeInTheDocument();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
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

    await screen.findByText('BS.CKII Trần Quốc Tuấn');

    const wednesdayOption = screen.getByRole('option', { name: /Thứ 4/i });
    fireEvent.click(wednesdayOption);
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Thứ 4/i })).toHaveAttribute(
        'aria-selected',
        'true',
      );
    });

    expect(
      await screen.findByText(/Buổi sáng bác sĩ có lịch mổ nội soi tiêu hóa tại khoa/),
    ).toBeInTheDocument();
    expect(screen.queryByText('CA KHÁM BUỔI SÁNG (07:30 - 11:30)')).not.toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI CHIỀU (13:30 - 16:30)')).toBeInTheDocument();

    const afternoonLastSlot = screen.getByRole('button', { name: /16:00 - 16:30/i });
    fireEvent.click(afternoonLastSlot);

    expect(
      screen.getByText(/16:00 - 16:30 • Thứ Tư, 14\/10\/2026 \(Ca Chiều\)/),
    ).toBeInTheDocument();
  });

  it('prevents selecting full or off-duty dates and full time slots', async () => {
    renderSlotBookingPage();

    await screen.findByText('BS.CKII Trần Quốc Tuấn');

    const fullDateOption = screen.getByRole('option', { name: /Thứ 2/i });
    const sundayOffOption = screen.getByRole('option', { name: /Chủ Nhật/i });
    expect(fullDateOption).toBeDisabled();
    expect(sundayOffOption).toBeDisabled();

    const fullMorningSlot = screen.getByRole('button', { name: /07:30 - 08:00/i });
    expect(fullMorningSlot).toBeDisabled();
  });

  it('records symptom notes and navigates to review step on continue', async () => {
    renderSlotBookingPage();

    await screen.findByText('BS.CKII Trần Quốc Tuấn');

    const textarea = screen.getByLabelText('Lý do khám / Triệu chứng ban đầu');
    fireEvent.change(textarea, {
      target: { value: 'Đau thượng vị âm ỉ 3 ngày nay' },
    });
    expect(useBookingStore.getState().symptomNote).toBe('Đau thượng vị âm ỉ 3 ngày nay');

    const continueButton = screen.getByRole('button', {
      name: /Tiếp tục: Soát xét Đặt lịch/i,
    });
    fireEvent.click(continueButton);

    expect(await screen.findByText('Soát xét Đặt lịch Mock')).toBeInTheDocument();
  });

  it('does not leak stale selectedServiceId from store and initializes dateIso to Asia/Ho_Chi_Minh today', async () => {
    const todayIso = getHospitalTodayIso();
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: getMockAvailableSlotsPayload('2', null, '2026-10-13'),
    });

    useBookingStore.getState().selectService('srv-th-01', 'Khám Chuyên khoa Tiêu Hóa');
    renderSlotBookingPage('/appointment/doctors/2/booking');

    expect(getSpy).toHaveBeenCalledWith('/appointments/available-slots', {
      params: {
        doctorId: '2',
        serviceId: null,
        dateIso: todayIso,
      },
    });
  });

  it('triggers error toast when selecting a date with an active booking or continuing without symptom notes', async () => {
    const mockPayload = getMockAvailableSlotsPayload('doc-01', null, '2026-10-13');
    mockPayload.profiles = mockPayload.profiles.map((p, idx) =>
      idx === 0 ? { ...p, activeBookedDates: ['2026-10-14'] } : p,
    );
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockPayload });

    renderSlotBookingPage('/appointment/doctors/doc-01/booking');

    const continueButton = await screen.findByRole('button', {
      name: /Tiếp tục: Soát xét Đặt lịch/i,
    });
    fireEvent.click(continueButton);

    expect(await screen.findByTestId('toast-error')).toHaveTextContent('Thiếu thông tin bắt buộc');
    expect(screen.queryByText('Soát xét Đặt lịch Mock')).not.toBeInTheDocument();

    toast.clear();

    const wednesdayOption = await screen.findByRole('option', { name: /Thứ 4/i });
    fireEvent.click(wednesdayOption);

    expect(await screen.findByTestId('toast-error')).toHaveTextContent(
      'Không thể đặt trùng ngày khám',
    );
  });

  it('separates empty slot state and API error state, blocking Continue when no valid slot exists', async () => {
    const emptyPayload = {
      ...getMockAvailableSlotsPayload('doc-01', null, '2026-10-13'),
      morningSlots: [],
      afternoonSlots: [],
    };
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: emptyPayload });

    const { unmount } = renderSlotBookingPage('/appointment/doctors/doc-01/booking');

    expect(
      await screen.findByText('Hiện không còn khung giờ khám trống trong ngày đã chọn'),
    ).toBeInTheDocument();
    const continueBtn = screen.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/i });
    expect(continueBtn).toBeDisabled();
    unmount();

    getSpy.mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'));
    renderSlotBookingPage('/appointment/doctors/doc-01/booking');

    expect(
      await screen.findByText('Không thể tải lịch khám khả dụng của bác sĩ'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thử tải lại/i })).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/i }),
    ).not.toBeInTheDocument();
  });
});
