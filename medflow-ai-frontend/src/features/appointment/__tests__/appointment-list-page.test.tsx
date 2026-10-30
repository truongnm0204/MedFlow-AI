import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as appointmentListApi from '../api/appointment-list.api';
import { AppointmentListPage } from '../pages/appointment-list-page';

function renderAppointmentListPage(initialEntry = '/appointments') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AppointmentListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentListPage (SCR-APT-08)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders page header, member filter pills, status tabs, and all 3 initial appointment cards matching Figma 17:676', async () => {
    renderAppointmentListPage();

    expect(screen.getByText('Lịch Hẹn Của Tôi')).toBeInTheDocument();
    expect(screen.getByText('Lịch hẹn của tôi (SCR-APT-08)')).toBeInTheDocument();

    expect(await screen.findByText('#APT-2026-1013-0902')).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-0815-0830')).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-0610-1400')).toBeInTheDocument();

    expect(screen.getByText('Tất cả hồ sơ (3 lịch)')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An (Bản thân - 2 lịch)')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Bảo Châu (Con gái - 1 lịch)')).toBeInTheDocument();
    expect(screen.getByText('Lê Thị Mai (Mẹ ruột - 0 lịch)')).toBeInTheDocument();
  });

  it('filters appointments by status tab and family member pill, showing empty state for 0-appointment member', async () => {
    renderAppointmentListPage();

    expect(await screen.findByText('#APT-2026-1013-0902')).toBeInTheDocument();

    // Switch to Completed tab
    fireEvent.click(screen.getByRole('tab', { name: /Đã hoàn thành/i }));
    await waitFor(() => {
      expect(screen.getByText('#APT-2026-0815-0830')).toBeInTheDocument();
      expect(screen.queryByText('#APT-2026-1013-0902')).toBeNull();
    });

    // Click member with 0 appointments -> shows empty state
    fireEvent.click(screen.getByText('Lê Thị Mai (Mẹ ruột - 0 lịch)'));
    expect(
      await screen.findByText('Không tìm thấy lịch hẹn phù hợp với bộ lọc'),
    ).toBeInTheDocument();

    // Reset filter -> restores all 3 appointments
    fireEvent.click(screen.getByText('Xóa bộ lọc'));
    expect(await screen.findByText('#APT-2026-1013-0902')).toBeInTheDocument();
  });

  it('validates date range filter on submit and shows inline validation error when dateFrom > dateTo', async () => {
    renderAppointmentListPage();

    expect(await screen.findByText('#APT-2026-1013-0902')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Từ ngày'), {
      target: { value: '2026-10-25' },
    });
    fireEvent.change(screen.getByLabelText('Đến ngày'), {
      target: { value: '2026-10-10' },
    });
    fireEvent.click(screen.getByText('Lọc lịch hẹn'));

    expect(screen.getByText('Ngày bắt đầu không được lớn hơn ngày kết thúc.')).toBeInTheDocument();
  });

  it('opens and closes the electronic appointment slip modal (SCR-APT-07B) when clicking Xem chi tiết phiếu hẹn & Mã QR', async () => {
    renderAppointmentListPage();

    const openBtn = await screen.findByText(/Xem chi tiết phiếu hẹn & Mã QR/i);
    fireEvent.click(openBtn);

    expect(screen.getByText('PHIẾU HẸN KHÁM NGOẠI TRÚ ĐIỆN TỬ (SCR-APT-07B)')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Đóng phiếu hẹn'));
    expect(screen.queryByText('PHIẾU HẸN KHÁM NGOẠI TRÚ ĐIỆN TỬ (SCR-APT-07B)')).toBeNull();
  });

  it('displays API error alert and recovers when clicking Thử lại', async () => {
    const spy = vi
      .spyOn(appointmentListApi, 'fetchPatientAppointmentsApi')
      .mockRejectedValueOnce(new Error('Máy chủ lịch hẹn đang bảo trì'))
      .mockResolvedValueOnce({
        items: [],
        total: 0,
        page: 1,
        pageSize: 10,
        totalPages: 1,
        tabCounts: { all: 0, upcoming: 0, completed: 0, cancelled: 0 },
        memberCounts: [],
        totalAllMembersCount: 0,
      });

    renderAppointmentListPage();

    expect(await screen.findByText('Máy chủ lịch hẹn đang bảo trì')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Thử lại'));

    expect(await screen.findByText('Chưa có lịch hẹn khám ngoại trú nào')).toBeInTheDocument();
    expect(spy).toHaveBeenCalledTimes(2);
  });
});
