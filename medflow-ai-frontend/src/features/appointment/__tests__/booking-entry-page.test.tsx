import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as hospitalApi from '../api/hospital.api';
import { MOCK_HOSPITALS } from '../data/hospital-mock';
import { BookingEntryPage } from '../pages/booking-entry-page';
import { useBookingStore } from '../store/booking.store';
import type { Hospital } from '../types/booking-entry.types';

function LocationProbe() {
  const { search } = useLocation();
  return <div data-testid="location-search">{search}</div>;
}

function renderBookingEntry(initialPath = '/booking') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/booking" element={<BookingEntryPage />} />
          <Route path="/appointment/book" element={<BookingEntryPage />} />
          <Route path="/doctors" element={<div>Danh bạ Bác sĩ Mock</div>} />
          <Route path="/services" element={<div>Danh mục Dịch vụ Mock</div>} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('BookingEntryPage (#619)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useBookingStore.getState().reset();
  });

  it('renders 3 hospital radios; Đà Nẵng disabled + "Tạm ngưng"; both mode buttons disabled', async () => {
    renderBookingEntry();

    const radios = await screen.findAllByRole('radio');
    expect(radios).toHaveLength(3);

    const daNang = screen.getByRole('radio', { name: /Cơ sở Đà Nẵng/ });
    expect(daNang).toBeDisabled();
    expect(screen.getByText('Tạm ngưng tiếp nhận')).toBeInTheDocument();

    expect(screen.getByRole('button', { name: /Khám theo Bác sĩ/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Khám theo Dịch vụ/ })).toBeDisabled();
  });

  it('chọn Cơ sở Hà Nội → Khám theo Bác sĩ → điều hướng /doctors?hospitalId=hos-01 + store DOCTOR', async () => {
    renderBookingEntry('/booking');

    fireEvent.click(await screen.findByRole('radio', { name: /Cơ sở Hà Nội/ }));
    expect(screen.getByRole('radio', { name: /Cơ sở Hà Nội/ })).toHaveAttribute(
      'aria-checked',
      'true',
    );

    const modeBtn = screen.getByRole('button', { name: /Khám theo Bác sĩ/ });
    await waitFor(() => expect(modeBtn).toBeEnabled());
    fireEvent.click(modeBtn);

    expect(await screen.findByText('Danh bạ Bác sĩ Mock')).toBeInTheDocument();
    expect(screen.getByTestId('location-search')).toHaveTextContent('?hospitalId=hos-01');
    expect(useBookingStore.getState().selectedHospitalId).toBe('hos-01');
    expect(useBookingStore.getState().bookingMode).toBe('DOCTOR');
  });

  it('route /appointment/book + Khám theo Dịch vụ → điều hướng /services?hospitalId=… + store SERVICE', async () => {
    renderBookingEntry('/appointment/book');

    fireEvent.click(await screen.findByRole('radio', { name: /Cơ sở Hà Nội/ }));
    const modeBtn = screen.getByRole('button', { name: /Khám theo Dịch vụ/ });
    await waitFor(() => expect(modeBtn).toBeEnabled());
    fireEvent.click(modeBtn);

    expect(await screen.findByText('Danh mục Dịch vụ Mock')).toBeInTheDocument();
    expect(useBookingStore.getState().bookingMode).toBe('SERVICE');
  });
  it('một cơ sở active duy nhất → mode buttons tự động bật (không cần click cơ sở)', async () => {
    const single: Hospital[] = [{ ...MOCK_HOSPITALS[0] }];
    vi.spyOn(hospitalApi, 'fetchHospitalsApi').mockResolvedValue(single);
    renderBookingEntry();

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Khám theo Bác sĩ/ })).toBeEnabled(),
    );
    expect(screen.getByRole('button', { name: /Khám theo Dịch vụ/ })).toBeEnabled();
  });

  it('lỗi API → hiển thị "Không thể tải danh sách cơ sở khám" + nút Thử lại', async () => {
    vi.spyOn(hospitalApi, 'fetchHospitalsApi').mockRejectedValue(new Error('x'));
    renderBookingEntry();

    expect(
      await screen.findByText(
        'Không thể tải danh sách cơ sở khám. Vui lòng kiểm tra kết nối và thử lại.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thử lại/ })).toBeInTheDocument();
  });
});
