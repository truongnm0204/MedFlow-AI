import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { getHospitalTodayIso } from '@/shared/lib/hospital-date';

import { getMockServiceAvailableShiftsPayload } from '../data/slot-booking-mock';
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
    vi.restoreAllMocks();
    useBookingStore.getState().reset();

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/appointments/service-shifts') {
        const params = (
          config as { params?: { serviceId?: string | null; dateIso?: string } } | undefined
        )?.params;
        const requestedDate =
          params?.dateIso === getHospitalTodayIso() ? '2026-10-13' : params?.dateIso;
        return {
          data: getMockServiceAvailableShiftsPayload(params?.serviceId, requestedDate),
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders service-only banner without doctor info and shows date + detailed time slot selection', async () => {
    renderServiceBookingPage();

    expect(screen.getAllByText('Dịch vụ khám').length).toBeGreaterThan(0);
    expect(screen.getByText('Ngày & Ca khám')).toBeInTheDocument();

    expect(await screen.findByText('DV-TH-01')).toBeInTheDocument();
    expect(screen.getAllByText('Khám Chuyên khoa Tiêu Hóa').length).toBeGreaterThan(0);
    expect(screen.getByText('Đổi dịch vụ khám')).toBeInTheDocument();
    expect(screen.getByText('+ Thêm hồ sơ người thân')).toBeInTheDocument();
    expect(screen.queryByText(/SCR-PRO-05/i)).not.toBeInTheDocument();

    expect(screen.queryByText('BS.CKII Trần Quốc Tuấn')).not.toBeInTheDocument();
    expect(screen.getByText('2. Chọn Ngày Khám')).toBeInTheDocument();
    expect(screen.getByText('3. Chọn Khung Giờ Thăm Khám')).toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI SÁNG (07:30 - 11:30)')).toBeInTheDocument();
    expect(screen.getByText('CA KHÁM BUỔI CHIỀU (13:30 - 16:30)')).toBeInTheDocument();
  });

  it('requires symptomNote before continuing and allows selecting a detailed afternoon slot', async () => {
    renderServiceBookingPage();

    const afternoonSlotButton = await screen.findByRole('button', { name: /16:00 - 16:30/i });
    fireEvent.click(afternoonSlotButton);

    expect(
      screen.getByText(/16:00 - 16:30 • Thứ Ba, 13\/10\/2026 \(Ca Chiều\)/),
    ).toBeInTheDocument();

    const continueBtn = screen.getByRole('button', {
      name: /Tiếp tục: Soát xét Đặt lịch/i,
    });

    fireEvent.click(continueBtn);
    expect(
      screen.getByText(/Vui lòng nhập lý do khám hoặc triệu chứng ban đầu/i),
    ).toBeInTheDocument();
    expect(screen.queryByText('Soát xét Đặt lịch Dịch vụ Mock')).not.toBeInTheDocument();

    const textarea = screen.getByLabelText('Lý do khám / Triệu chứng ban đầu');
    fireEvent.change(textarea, {
      target: { value: 'Đau thượng vị và đầy hơi sau ăn' },
    });

    fireEvent.click(continueBtn);
    expect(await screen.findByText('Soát xét Đặt lịch Dịch vụ Mock')).toBeInTheDocument();
  });

  it('shows distinct empty and error states and disables Continue when no valid slot or shift exists', async () => {
    const emptyPayload = {
      ...getMockServiceAvailableShiftsPayload('srv-th-01', '2026-10-13'),
      morningSlots: [],
      afternoonSlots: [],
      shifts: [],
    };
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: emptyPayload });

    const { unmount } = renderServiceBookingPage();

    expect(
      await screen.findByText('Hiện không còn ca hoặc khung giờ khám trống trong ngày đã chọn'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/i })).toBeDisabled();
    unmount();

    getSpy.mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'));
    renderServiceBookingPage();

    expect(await screen.findByText('Không thể tải lịch ca khám của dịch vụ')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thử tải lại/i })).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/i }),
    ).not.toBeInTheDocument();
  });
});
